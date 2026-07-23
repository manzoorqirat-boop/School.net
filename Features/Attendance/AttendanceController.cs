using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Attendance;

[ApiController]
[Route("api/attendance")]
public sealed class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public AttendanceController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    // ── GET /api/attendance/roster ────────────────────────────────────────

    [HttpGet("roster")]
    [RequirePrivilege("attendance:view")]
    public async Task<IActionResult> Roster(
        [FromQuery] string? @class, [FromQuery] string? section, [FromQuery] DateOnly? date,
        [FromQuery] string mode = "daily", [FromQuery] int? period = null, [FromQuery] string? subject = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@class) || string.IsNullOrEmpty(section))
            return BadRequest(new { error = "class and section required" });

        if (!await CanMarkClassAsync(@class, section, subject, mode, ct))
            return StatusCode(403, new { error = "You are not assigned to this class/section", code = "NOT_ASSIGNED" });

        var target = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var isPeriod = mode == "period";

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.Class == @class && s.Section == section && s.Status == StudentStatus.Active)
            .OrderBy(s => s.RollNo).ThenBy(s => s.FirstName)
            .ToListAsync(ct);

        var q = _db.Attendance.AsNoTracking()
            .Where(a => a.Class == @class && a.Section == section
                     && a.Date == target && a.Mode == (isPeriod ? AttendanceMode.Period : AttendanceMode.Daily));
        if (isPeriod)
        {
            if (period is { } p) q = q.Where(a => a.Period == (short)p);
            if (!string.IsNullOrEmpty(subject)) q = q.Where(a => a.Subject == subject);
        }
        var records = await q.ToListAsync(ct);

        var byStudent = records.ToDictionary(r => r.StudentId);
        DateTime? lastMarkedAt = null; string? lastMarkedBy = null;
        foreach (var r in records)
        {
            var when = r.UpdatedAt == default ? r.CreatedAt : r.UpdatedAt;
            if (lastMarkedAt is null || when > lastMarkedAt) { lastMarkedAt = when; lastMarkedBy = r.MarkedByName; }
        }

        var roster = students.Select(s => new
        {
            student = new { _id = s.Id, s.AdmissionNo, s.RollNo, s.FirstName, s.LastName, s.PhotoUrl },
            attendance = byStudent.GetValueOrDefault(s.Id),
        });

        return Ok(new
        {
            @class, section, date = target, mode,
            period = isPeriod ? period : null,
            subject = string.IsNullOrEmpty(subject) ? null : subject,
            count = students.Count,
            roster,
            lastMarkedAt, lastMarkedBy,
        });
    }

    // ── POST /api/attendance/mark-bulk ────────────────────────────────────

    public sealed record MarkEntry(Guid StudentId, string? Status, string? Remarks,
                                   DateTime? ArrivedAt, string? LeaveReason);
    public sealed record MarkBulkRequest(
        string? Class, string? Section, DateOnly? Date, string Mode = "daily",
        int? Period = null, string? Subject = null, string? AcademicYear = null,
        List<MarkEntry>? Entries = null);

    [HttpPost("mark-bulk")]
    [RequirePrivilege("attendance:mark")]
    public async Task<IActionResult> MarkBulk([FromBody] MarkBulkRequest req, CancellationToken ct)
    {
        var academicYear = req.AcademicYear;
        if (string.IsNullOrEmpty(academicYear))
            academicYear = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == _tenant.SchoolId).Select(s => s.AcademicYear).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(academicYear))
            return BadRequest(new { error = "academicYear required" });

        if (string.IsNullOrEmpty(req.Class) || string.IsNullOrEmpty(req.Section))
            return BadRequest(new { error = "class and section required" });
        if (req.Entries is null || req.Entries.Count == 0)
            return BadRequest(new { error = "entries array required" });

        var isPeriod = req.Mode == "period";
        if (isPeriod && req.Period is null)
            return BadRequest(new { error = "period required for period-wise attendance" });

        if (!await CanMarkClassAsync(req.Class, req.Section, req.Subject, req.Mode, ct))
            return StatusCode(403, new { error = "You are not assigned to this class/section" });

        var target = req.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        int created = 0, updated = 0, unchanged = 0;
        var errors = new List<object>();

        foreach (var e in req.Entries)
        {
            if (e.StudentId == Guid.Empty || string.IsNullOrEmpty(e.Status))
            {
                errors.Add(new { studentId = e.StudentId, error = "studentId and status required" });
                continue;
            }
            if (!Enum.TryParse<AttendanceStatus>(e.Status, true, out var status))
            {
                errors.Add(new { studentId = e.StudentId, error = $"invalid status '{e.Status}'" });
                continue;
            }

            try
            {
                // Find by the SAME composite key the partial unique index enforces
                // (daily: school+student+date; period: +period+subject). This is
                // the app-side half of the NULL-trap protection — the DB index is
                // the guarantee, this avoids the round-trip to a 23505.
                var existing = await _db.Attendance.FirstOrDefaultAsync(a =>
                    a.StudentId == e.StudentId && a.Date == target
                    && a.Mode == (isPeriod ? AttendanceMode.Period : AttendanceMode.Daily)
                    && (!isPeriod || (a.Period == (short?)req.Period && a.Subject == req.Subject)), ct);

                if (existing is null)
                {
                    _db.Attendance.Add(new Domain.Entities.Attendance
                    {
                        SchoolId = _tenant.SchoolId ?? Guid.Empty,
                        StudentId = e.StudentId,
                        AcademicYear = academicYear,
                        Class = req.Class, Section = req.Section,
                        Date = target,
                        Mode = isPeriod ? AttendanceMode.Period : AttendanceMode.Daily,
                        Period = isPeriod ? (short?)req.Period : null,
                        Subject = isPeriod ? req.Subject : null,
                        Status = status,
                        Remarks = e.Remarks,
                        ArrivedAt = e.ArrivedAt,
                        LeaveReason = e.LeaveReason,
                        MarkedByUserId = _tenant.UserId,
                        MarkedByName = User.Identity?.Name,
                    });
                    await _db.SaveChangesAsync(ct);
                    created++;
                }
                // Change-detection must cover every field the update body writes.
                // Previously this only compared Status and Remarks, so a correction
                // that touched only ArrivedAt (late arrival time) or LeaveReason was
                // counted as "unchanged" and silently never persisted.
                else if (existing.Status != status
                      || existing.Remarks != e.Remarks
                      || existing.ArrivedAt != e.ArrivedAt
                      || existing.LeaveReason != e.LeaveReason)
                {
                    existing.Status = status;
                    existing.Remarks = e.Remarks;
                    existing.ArrivedAt = e.ArrivedAt;
                    existing.LeaveReason = e.LeaveReason;
                    existing.MarkedByUserId = _tenant.UserId;
                    existing.MarkedByName = User.Identity?.Name;
                    await _db.SaveChangesAsync(ct);
                    updated++;
                }
                else unchanged++;
            }
            catch (Exception ex)
            {
                errors.Add(new { studentId = e.StudentId, error = ex.Message });
            }
        }

        await _audit.WriteAsync("attendance.mark_bulk", "attendance",
            metaJson: System.Text.Json.JsonSerializer.Serialize(
                new { req.Class, req.Section, date = target, req.Mode, req.Period, created, updated, unchanged }),
            ct: ct);

        return Ok(new { created, updated, unchanged, errors });
    }

    // ── GET /api/attendance/student/:studentId ────────────────────────────

    [HttpGet("student/{studentId:guid}")]
    [RequirePrivilege("attendance:view")]
    public async Task<IActionResult> StudentHistory(
        Guid studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? mode, [FromQuery] int? page, [FromQuery] int? limit,
        CancellationToken ct)
    {
        // Row-scope: parent must own this student; student must be self.
        if (_tenant.Role == "parent" &&
            !(await ParentOfIdsAsync(ct)).Contains(studentId))
            return StatusCode(403, new { error = "Forbidden" });
        if (_tenant.Role == "student" && _tenant.StudentId != studentId)
            return StatusCode(403, new { error = "Forbidden" });

        var q = _db.Attendance.AsNoTracking().Where(a => a.StudentId == studentId);
        if (!string.IsNullOrEmpty(mode) && Enum.TryParse<AttendanceMode>(mode, true, out var m))
            q = q.Where(a => a.Mode == m);
        if (from is { } f) q = q.Where(a => a.Date >= f);
        if (to is { } t) q = q.Where(a => a.Date <= t);

        // Status summary over the WHOLE filtered set (not just the page).
        var statusCounts = await q
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int present = 0, absent = 0, late = 0, leave = 0, holiday = 0;
        foreach (var r in statusCounts)
            switch (r.Status)
            {
                case AttendanceStatus.Present: present = r.Count; break;
                case AttendanceStatus.Absent: absent = r.Count; break;
                case AttendanceStatus.Late: late = r.Count; break;
                case AttendanceStatus.Leave: leave = r.Count; break;
                case AttendanceStatus.Holiday: holiday = r.Count; break;
            }

        var workingDays = present + absent + late + leave;   // holiday excluded
        var summary = new
        {
            present, absent, late, leave, holiday,
            total = workingDays,
            percentage = AttendanceRules.Percentage(present, late, workingDays),
        };

        var p = Math.Max(1, page ?? 1);
        var l = Math.Clamp(limit ?? PageInfo.DefaultLimit, 1, 100);
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(a => a.Date).ThenBy(a => a.Period)
            .Skip((p - 1) * l).Take(l).ToListAsync(ct);

        // items + pagination + summary, plus the records/count aliases the
        // attendance & payroll pages read. Built explicitly to carry all five.
        return Ok(new
        {
            items,
            pagination = PageInfo.Create(total, p, l),
            summary,
            records = items,
            count = total,
        });
    }

    // ── GET /api/attendance/reports/class ─────────────────────────────────

    [HttpGet("reports/class")]
    [RequirePrivilege("attendance:report")]
    public async Task<IActionResult> ClassReport(
        [FromQuery] string? @class, [FromQuery] string? section,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string mode = "daily", CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@class) || string.IsNullOrEmpty(section))
            return BadRequest(new { error = "class and section required" });

        var m = mode == "period" ? AttendanceMode.Period : AttendanceMode.Daily;
        var q = _db.Attendance.AsNoTracking()
            .Where(a => a.Class == @class && a.Section == section && a.Mode == m);
        if (from is { } f) q = q.Where(a => a.Date >= f);
        if (to is { } t) q = q.Where(a => a.Date <= t);

        var grouped = await q
            .GroupBy(a => new { a.StudentId, a.Status })
            .Select(g => new { g.Key.StudentId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var byStudent = grouped.GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Status, x => x.Count));

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.Class == @class && s.Section == section && s.Status == StudentStatus.Active)
            .OrderBy(s => s.RollNo).ThenBy(s => s.FirstName)
            .ToListAsync(ct);

        var rows = students.Select(s =>
        {
            var c = byStudent.GetValueOrDefault(s.Id) ?? new();
            int present = c.GetValueOrDefault(AttendanceStatus.Present);
            int absent = c.GetValueOrDefault(AttendanceStatus.Absent);
            int late = c.GetValueOrDefault(AttendanceStatus.Late);
            int leave = c.GetValueOrDefault(AttendanceStatus.Leave);
            var working = present + absent + late + leave;
            return new
            {
                student = new { _id = s.Id, s.AdmissionNo, s.RollNo, s.FirstName, s.LastName },
                present, absent, late, leave,
                workingDays = working,
                percentage = AttendanceRules.Percentage(present, late, working),
            };
        });

        return Ok(new { @class, section, mode, rows });
    }

    // ── GET /api/attendance/reports/daily-summary ─────────────────────────

    [HttpGet("reports/daily-summary")]
    [RequirePrivilege("attendance:report")]
    public async Task<IActionResult> DailySummary(
        [FromQuery] DateOnly? date, [FromQuery] string mode = "daily", CancellationToken ct = default)
    {
        var target = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var m = mode == "period" ? AttendanceMode.Period : AttendanceMode.Daily;

        var counts = await _db.Attendance.AsNoTracking()
            .Where(a => a.Date == target && a.Mode == m)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var map = counts.ToDictionary(x => x.Status, x => x.Count);
        int present = map.GetValueOrDefault(AttendanceStatus.Present);
        int late = map.GetValueOrDefault(AttendanceStatus.Late);
        int absent = map.GetValueOrDefault(AttendanceStatus.Absent);
        int leave = map.GetValueOrDefault(AttendanceStatus.Leave);
        var working = present + absent + late + leave;

        return Ok(new
        {
            date = target, mode,
            present, absent, late, leave,
            holiday = map.GetValueOrDefault(AttendanceStatus.Holiday),
            marked = working,
            percentage = AttendanceRules.Percentage(present, late, working),
        });
    }

    // ── GET /api/attendance/reports/trends ────────────────────────────────
    // Per-day attendance percentage for a class/section over a date range.
    // Ported from Node ctrl.trends: group by date×status → one row per day.
    [HttpGet("reports/trends")]
    [RequirePrivilege("attendance:report")]
    public async Task<IActionResult> Trends(
        [FromQuery] string? @class, [FromQuery] string? section,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int? period, [FromQuery] string? subject,
        [FromQuery] string mode = "daily",
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@class) || string.IsNullOrEmpty(section))
            return BadRequest(new { error = "class and section required" });

        var isPeriod = mode == "period";
        var m = isPeriod ? AttendanceMode.Period : AttendanceMode.Daily;

        var q = _db.Attendance.AsNoTracking()
            .Where(a => a.Class == @class && a.Section == section && a.Mode == m);
        if (from is { } f) q = q.Where(a => a.Date >= f);
        if (to is { } t) q = q.Where(a => a.Date <= t);
        if (isPeriod)
        {
            if (period is { } p) q = q.Where(a => a.Period == (short)p);
            if (!string.IsNullOrEmpty(subject)) q = q.Where(a => a.Subject == subject);
        }

        var grouped = await q
            .GroupBy(a => new { a.Date, a.Status })
            .Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var rows = grouped
            .GroupBy(x => x.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var c = g.ToDictionary(x => x.Status, x => x.Count);
                int present = c.GetValueOrDefault(AttendanceStatus.Present);
                int absent = c.GetValueOrDefault(AttendanceStatus.Absent);
                int late = c.GetValueOrDefault(AttendanceStatus.Late);
                int leave = c.GetValueOrDefault(AttendanceStatus.Leave);
                int holiday = c.GetValueOrDefault(AttendanceStatus.Holiday);
                var total = present + absent + late + leave;   // holiday excluded
                return new
                {
                    date = g.Key, present, absent, late, leave, holiday, total,
                    percentage = total == 0 ? 0d : Math.Round((present + late) / (double)total * 1000) / 10,
                };
            })
            .ToList();

        return Ok(new { @class, section, mode, from, to, rows });
    }

    // ── GET /api/attendance/reports/period-breakdown ──────────────────────
    // Period-mode only: absentee counts per period across the date range.
    [HttpGet("reports/period-breakdown")]
    [RequirePrivilege("attendance:report")]
    public async Task<IActionResult> PeriodBreakdown(
        [FromQuery] string? @class, [FromQuery] string? section,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? subject,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@class) || string.IsNullOrEmpty(section))
            return BadRequest(new { error = "class and section required" });

        var q = _db.Attendance.AsNoTracking()
            .Where(a => a.Class == @class && a.Section == section && a.Mode == AttendanceMode.Period);
        if (from is { } f) q = q.Where(a => a.Date >= f);
        if (to is { } t) q = q.Where(a => a.Date <= t);
        if (!string.IsNullOrEmpty(subject)) q = q.Where(a => a.Subject == subject);

        var grouped = await q
            .GroupBy(a => new { a.Period, a.Status })
            .Select(g => new { g.Key.Period, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var rows = grouped
            .GroupBy(x => x.Period)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var c = g.ToDictionary(x => x.Status, x => x.Count);
                int present = c.GetValueOrDefault(AttendanceStatus.Present);
                int absent = c.GetValueOrDefault(AttendanceStatus.Absent);
                int late = c.GetValueOrDefault(AttendanceStatus.Late);
                int leave = c.GetValueOrDefault(AttendanceStatus.Leave);
                int holiday = c.GetValueOrDefault(AttendanceStatus.Holiday);
                var total = present + absent + late + leave;
                return new
                {
                    period = g.Key, present, absent, late, leave, holiday, total,
                    percentage = total == 0 ? 0d : Math.Round((present + late) / (double)total * 1000) / 10,
                };
            })
            .ToList();

        return Ok(new { @class, section, mode = "period", from, to, rows });
    }

    // ── shared: teacher-assignment gate (canMarkClass) ────────────────────

    private async Task<bool> CanMarkClassAsync(
        string cls, string section, string? subject, string mode, CancellationToken ct)
    {
        if (_tenant.Role is "school_admin" or "principal" or "superadmin") return true;
        if (_tenant.Role != "teacher" || _tenant.UserId is not { } uid) return false;

        var assignments = await _db.ClassTeachers.AsNoTracking()
            .Where(a => a.TeacherUserId == uid && a.Class == cls
                     && a.Section == section && a.IsActive)
            .ToListAsync(ct);

        if (assignments.Count == 0) return false;

        if (mode == "period")
        {
            if (string.IsNullOrEmpty(subject)) return false;
            var want = subject.Trim().ToLowerInvariant();
            return assignments.Any(a =>
            {
                var subj = (a.Subject ?? "").Trim().ToLowerInvariant();
                return subj == want || subj == "" || a.IsPrimary;
            });
        }
        return true;   // daily: any active assignment to the class suffices
    }

    private async Task<IReadOnlyCollection<Guid>> ParentOfIdsAsync(CancellationToken ct)
    {
        if (_tenant.Role != "parent" || _tenant.UserId is not { } uid)
            return Array.Empty<Guid>();
        return await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == uid)
            .SelectMany(u => u.ParentOf.Select(s => s.Id))
            .ToListAsync(ct);
    }
}
