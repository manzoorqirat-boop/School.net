using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Attendance;

[ApiController]
[Route("api/teacher-attendance")]
public sealed class TeacherAttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public TeacherAttendanceController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet("roster")]
    [RequirePrivilege("teacher_attendance:mark")]
    public async Task<IActionResult> Roster([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var target = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var academicYear = await _db.Schools.AsNoTracking()
            .Where(s => s.Id == _tenant.SchoolId).Select(s => s.AcademicYear).FirstOrDefaultAsync(ct);

        var teachers = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Teacher && u.IsActive)
            .OrderBy(u => u.Name).ToListAsync(ct);

        var records = await _db.TeacherAttendance.AsNoTracking()
            .Where(a => a.Date == target).ToListAsync(ct);
        var byTeacher = records.ToDictionary(r => r.TeacherId);

        DateTime? lastAt = null; string? lastBy = null;
        foreach (var r in records)
        {
            var when = r.UpdatedAt == default ? r.CreatedAt : r.UpdatedAt;
            if (lastAt is null || when > lastAt) { lastAt = when; lastBy = r.MarkedByName; }
        }

        var roster = teachers.Select(t => new
        {
            teacher = new { _id = t.Id, t.Name, t.Username, t.Email },
            attendance = byTeacher.GetValueOrDefault(t.Id),
        });

        return Ok(new { date = target, academicYear, count = teachers.Count, roster, lastMarkedAt = lastAt, lastMarkedBy = lastBy });
    }

    public sealed record TAEntry(Guid TeacherId, string? Status, DateTime? CheckIn, DateTime? CheckOut, string? OnDutyNote, string? Remarks);
    public sealed record TAMarkRequest(DateOnly? Date, string? AcademicYear, List<TAEntry>? Entries);

    [HttpPost("mark-bulk")]
    [RequirePrivilege("teacher_attendance:mark")]
    public async Task<IActionResult> MarkBulk([FromBody] TAMarkRequest req, CancellationToken ct)
    {
        var academicYear = req.AcademicYear;
        if (string.IsNullOrEmpty(academicYear))
            academicYear = await _db.Schools.AsNoTracking()
                .Where(s => s.Id == _tenant.SchoolId).Select(s => s.AcademicYear).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(academicYear)) return BadRequest(new { error = "academicYear required" });
        if (req.Entries is null || req.Entries.Count == 0) return BadRequest(new { error = "entries array required" });

        var target = req.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var ids = req.Entries.Where(e => e.TeacherId != Guid.Empty).Select(e => e.TeacherId).ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Role == UserRole.Teacher)
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        int created = 0, updated = 0, unchanged = 0; var errors = new List<object>();
        foreach (var e in req.Entries)
        {
            if (e.TeacherId == Guid.Empty || string.IsNullOrEmpty(e.Status))
            { errors.Add(new { teacherId = e.TeacherId, error = "teacherId and status required" }); continue; }
            if (!Enum.TryParse<TeacherAttendanceStatus>(e.Status, true, out var st))
            { errors.Add(new { teacherId = e.TeacherId, error = $"Invalid status '{e.Status}'" }); continue; }
            if (!names.TryGetValue(e.TeacherId, out var tname))
            { errors.Add(new { teacherId = e.TeacherId, error = "Teacher not found in this school" }); continue; }

            var existing = await _db.TeacherAttendance
                .FirstOrDefaultAsync(a => a.TeacherId == e.TeacherId && a.Date == target, ct);
            if (existing is null)
            {
                _db.TeacherAttendance.Add(new TeacherAttendance
                {
                    SchoolId = _tenant.SchoolId ?? Guid.Empty, TeacherId = e.TeacherId,
                    AcademicYear = academicYear, Date = target, Status = st,
                    CheckIn = e.CheckIn, CheckOut = e.CheckOut, OnDutyNote = e.OnDutyNote,
                    Remarks = e.Remarks, TeacherName = tname,
                    MarkedByUserId = _tenant.UserId, MarkedByName = User.Identity?.Name,
                });
                await _db.SaveChangesAsync(ct); created++;
            }
            // Change-detection must cover every field the update body writes.
            // Previously this only compared Status and Remarks, so an edit that
            // touched only CheckIn, CheckOut or OnDutyNote was counted as
            // "unchanged" and silently never persisted.
            else if (existing.Status != st
                  || existing.Remarks != e.Remarks
                  || existing.CheckIn != e.CheckIn
                  || existing.CheckOut != e.CheckOut
                  || existing.OnDutyNote != e.OnDutyNote)
            {
                existing.Status = st; existing.CheckIn = e.CheckIn; existing.CheckOut = e.CheckOut;
                existing.OnDutyNote = e.OnDutyNote; existing.Remarks = e.Remarks;
                existing.MarkedByUserId = _tenant.UserId; existing.MarkedByName = User.Identity?.Name;
                await _db.SaveChangesAsync(ct); updated++;
            }
            else unchanged++;
        }

        await _audit.WriteAsync("teacher_attendance.mark_bulk", "teacher_attendance",
            metaJson: System.Text.Json.JsonSerializer.Serialize(new { date = target, created, updated, unchanged }), ct: ct);
        return Ok(new { created, updated, unchanged, errors });
    }

    // Monthly report — the numbers payroll reads (unpaid-day weights).
    [HttpGet("reports/monthly")]
    [RequirePrivilege("teacher_attendance:report")]
    public async Task<IActionResult> Monthly([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (month is < 1 or > 12) return BadRequest(new { error = "valid month required" });
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var rows = await _db.TeacherAttendance.AsNoTracking()
            .Where(a => a.Date >= start && a.Date <= end)
            .GroupBy(a => new { a.TeacherId, a.Status })
            .Select(g => new { g.Key.TeacherId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var byTeacher = rows.GroupBy(r => r.TeacherId).Select(g =>
        {
            var statuses = g.ToDictionary(x => x.Status, x => x.Count);
            var unpaid = statuses.Sum(kv => TeacherAttendanceRules.UnpaidWeight[kv.Key] * kv.Value);
            return new { teacherId = g.Key, statuses = statuses.ToDictionary(k => k.Key.ToString(), v => v.Value), unpaidDays = unpaid };
        });
        return Ok(new { year, month, teachers = byTeacher });
    }

    [HttpGet("teacher/{teacherId:guid}")]
    [RequirePrivilege("teacher_attendance:view")]
    public async Task<IActionResult> History(Guid teacherId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var q = _db.TeacherAttendance.AsNoTracking().Where(a => a.TeacherId == teacherId);
        if (from is { } f) q = q.Where(a => a.Date >= f);
        if (to is { } t) q = q.Where(a => a.Date <= t);
        var items = await q.OrderByDescending(a => a.Date).ToListAsync(ct);
        return Ok(new { items, count = items.Count });
    }
}
