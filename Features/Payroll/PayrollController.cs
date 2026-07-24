using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Payroll;

[ApiController]
[Route("api/payroll")]
public sealed class PayrollController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public PayrollController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    // ── Salary structures ─────────────────────────────────────────────────
    [HttpGet("structures")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> ListStructures([FromQuery] Guid? teacherId, CancellationToken ct)
    {
        var q = _db.SalaryStructures.AsNoTracking().AsQueryable();
        if (teacherId is { } t) q = q.Where(s => s.TeacherId == t);
        // Bare array — page does (list || []).
        return Ok(await q.Where(s => s.IsActive).ToListAsync(ct));
    }

    [HttpPost("structures")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> CreateStructure([FromBody] SalaryStructure body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.SalaryStructures.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("salary_structure.create", "salary_structure", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("structures/{id:guid}")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> UpdateStructure(Guid id, [FromBody] SalaryStructure body, CancellationToken ct)
    {
        var s = await _db.SalaryStructures.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        s.BaseSalary = body.BaseSalary; s.Da = body.Da; s.Hra = body.Hra; s.Ta = body.Ta;
        s.Pf = body.Pf; s.Esi = body.Esi; s.ProfessionalTax = body.ProfessionalTax; s.IncomeTax = body.IncomeTax;
        s.OtherAllowances = body.OtherAllowances; s.OtherDeductions = body.OtherDeductions;
        s.LeaveDeductionPerDay = body.LeaveDeductionPerDay;
        s.BankAccountNumber = body.BankAccountNumber; s.BankIfsc = body.BankIfsc; s.BankAccountHolder = body.BankAccountHolder;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("salary_structure.update", "salary_structure", id.ToString(), ct: ct);
        return Ok(s);
    }

    public sealed record CopyStructureRequest(Guid FromTeacherId, Guid ToTeacherId, string AcademicYear);
    [HttpPost("structures/copy")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> CopyStructure([FromBody] CopyStructureRequest req, CancellationToken ct)
    {
        var src = await _db.SalaryStructures.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TeacherId == req.FromTeacherId && s.AcademicYear == req.AcademicYear, ct);
        if (src is null) return NotFound(new { error = "Source structure not found" });
        var copy = new SalaryStructure
        {
            SchoolId = _tenant.SchoolId ?? Guid.Empty, TeacherId = req.ToTeacherId, AcademicYear = req.AcademicYear,
            EffectiveFrom = src.EffectiveFrom, BaseSalary = src.BaseSalary, Da = src.Da, Hra = src.Hra, Ta = src.Ta,
            Pf = src.Pf, Esi = src.Esi, ProfessionalTax = src.ProfessionalTax, IncomeTax = src.IncomeTax,
            OtherAllowances = src.OtherAllowances, OtherDeductions = src.OtherDeductions,
            LeaveDeductionPerDay = src.LeaveDeductionPerDay,
        };
        _db.SalaryStructures.Add(copy);
        await _db.SaveChangesAsync(ct);
        return StatusCode(201, copy);
    }

    // ── Leaves ────────────────────────────────────────────────────────────
    [HttpGet("leaves")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> GetLeaves([FromQuery] Guid? teacherId, [FromQuery] string? academicYear, CancellationToken ct)
    {
        var q = _db.Leaves.AsNoTracking().Include(l => l.Types).Include(l => l.Records).AsQueryable();
        if (teacherId is { } t) q = q.Where(l => l.TeacherId == t);
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(l => l.AcademicYear == academicYear);
        return Ok(new { items = await q.ToListAsync(ct) });
    }

    public sealed record ApplyLeaveRequest(Guid TeacherId, string AcademicYear, string Type, Guid? LeaveTypeId,
        DateOnly FromDate, DateOnly ToDate, decimal Days, string? Reason);
    [HttpPost("leaves/apply")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> ApplyLeave([FromBody] ApplyLeaveRequest req, CancellationToken ct)
    {
        var leave = await _db.Leaves.Include(l => l.Records)
            .FirstOrDefaultAsync(l => l.TeacherId == req.TeacherId && l.AcademicYear == req.AcademicYear, ct);
        if (leave is null)
        {
            leave = new Leave { SchoolId = _tenant.SchoolId ?? Guid.Empty, TeacherId = req.TeacherId, AcademicYear = req.AcademicYear };
            _db.Leaves.Add(leave);
        }
        leave.Records.Add(new LeaveRecord
        {
            Type = req.Type, LeaveTypeId = req.LeaveTypeId, FromDate = req.FromDate, ToDate = req.ToDate,
            Days = req.Days, Reason = req.Reason, Status = LeaveStatus.Pending, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("leave.apply", "leave", leave.Id.ToString(), ct: ct);
        return StatusCode(201, leave);
    }

    [HttpPost("leaves/{leaveDocId:guid}/records/{recordId:guid}/approve")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> ApproveLeave(Guid leaveDocId, Guid recordId, CancellationToken ct) =>
        await SetLeaveStatus(leaveDocId, recordId, LeaveStatus.Approved, ct);

    [HttpPost("leaves/{leaveDocId:guid}/records/{recordId:guid}/reject")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> RejectLeave(Guid leaveDocId, Guid recordId, CancellationToken ct) =>
        await SetLeaveStatus(leaveDocId, recordId, LeaveStatus.Rejected, ct);

    private async Task<IActionResult> SetLeaveStatus(Guid leaveDocId, Guid recordId, LeaveStatus status, CancellationToken ct)
    {
        var rec = await _db.LeaveRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.LeaveId == leaveDocId, ct);
        if (rec is null) return NotFound(new { error = "Not found" });
        rec.Status = status; rec.ApprovedByUserId = _tenant.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync($"leave.{status.ToString().ToLowerInvariant()}", "leave", recordId.ToString(), ct: ct);
        return Ok(rec);
    }

    // ── Payslip generation ────────────────────────────────────────────────
    public sealed record GenerateTeacherRequest(Guid TeacherId, int Month, int Year, string? AcademicYear);

    public sealed record GenerateRunRequest(int Month, int Year, string? AcademicYear, bool Force = false);

    /// <summary>
    /// Bulk payroll run for every active teacher. Restores POST /api/payroll/generate
    /// from the Node backend — the port only had generate/teacher, so a school with
    /// 40 staff had to generate 40 payslips one at a time and no PayrollRun row was
    /// ever created (leaving /runs permanently empty and bank transfers impossible).
    ///
    /// Idempotent: re-running a month reuses the existing PayrollRun rather than
    /// tripping the unique index, and per-teacher regeneration still honours
    /// GuardRegeneration, so locked or already-paid payslips are skipped with a
    /// reason instead of being silently overwritten.
    /// </summary>
    [HttpPost("generate")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> GenerateRun([FromBody] GenerateRunRequest req, CancellationToken ct)
    {
        if (req.Month is < 1 or > 12) return BadRequest(new { error = "Valid month (1-12) required" });
        if (req.Year is < 2000 or > 2100) return BadRequest(new { error = "Valid year required" });

        var school = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _tenant.SchoolId, ct);
        var academicYear = !string.IsNullOrWhiteSpace(req.AcademicYear)
            ? req.AcademicYear!
            : school?.AcademicYear ?? "";

        var teachers = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Teacher && u.IsActive)
            .OrderBy(u => u.Name).ToListAsync(ct);
        if (teachers.Count == 0) return BadRequest(new { error = "No active teachers found" });

        // Reuse an existing run for this month so totals accumulate against it.
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(
            r => r.Month == req.Month && r.Year == req.Year, ct);
        if (run is null)
        {
            run = new PayrollRun
            {
                Name = $"{System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(req.Month)} {req.Year} Payroll",
                Month = req.Month, Year = req.Year,
                AcademicYear = academicYear,
                InitiatedByUserId = _tenant.UserId,
            };
            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync(ct);   // need run.Id for the payslips below
        }
        run.TotalTeachers = teachers.Count;
        run.AcademicYear ??= academicYear;
        run.Skipped.Clear();

        var generated = 0;
        foreach (var teacher in teachers)
        {
            try
            {
                var ss = await _db.SalaryStructures.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TeacherId == teacher.Id && x.IsActive, ct);
                if (ss is null)
                {
                    run.Skipped.Add(new SkippedTeacher
                    { TeacherId = teacher.Id, Name = teacher.Name, Reason = "No active salary structure" });
                    continue;
                }

                var existing = await _db.Payrolls.FirstOrDefaultAsync(
                    x => x.TeacherId == teacher.Id && x.Year == req.Year && x.Month == req.Month, ct);
                if (existing is not null)
                {
                    try
                    {
                        // Throws when locked/paid unless force — that is a skip,
                        // not a failed run.
                        existing.GuardRegeneration(req.Force);
                    }
                    catch (Exception ex)
                    {
                        run.Skipped.Add(new SkippedTeacher
                        { TeacherId = teacher.Id, Name = teacher.Name, Reason = ex.Message });
                        continue;
                    }
                    _db.Payrolls.Remove(existing);
                }

                var unpaid = await ComputeUnpaidDaysAsync(teacher.Id, req.Year, req.Month, ct);
                var payslip = Domain.Entities.Payroll.BuildPayslip(
                    ss, teacher, req.Month, req.Year, academicYear, unpaid);
                payslip.PayrollRunId = run.Id;
                _db.Payrolls.Add(payslip);
                generated++;
            }
            catch (Exception ex)
            {
                // One bad structure must not abort the other 39 payslips.
                run.Skipped.Add(new SkippedTeacher
                { TeacherId = teacher.Id, Name = teacher.Name, Reason = ex.Message });
            }
        }

        run.PayrollsSkipped = run.Skipped.Count;
        run.Status = PayrollRunStatus.Generated;
        run.GeneratedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Recompute aggregates from the payslip rows, not from the loop counters —
        // a re-run must not double-count what a previous run already wrote.
        var rows = await _db.Payrolls.AsNoTracking()
            .Where(x => x.PayrollRunId == run.Id).ToListAsync(ct);
        run.PayrollsGenerated = rows.Count;
        run.PayrollsLocked = rows.Count(x => x.Status == PayrollStatus.Locked);
        run.PayrollsPaid = rows.Count(x => x.Status == PayrollStatus.Paid);
        run.TotalGross = rows.Sum(x => x.GrossSalary);
        run.TotalDeductions = rows.Sum(x => x.TotalDeductions);
        run.TotalNet = rows.Sum(x => x.NetSalary);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("payroll.generate", "payroll_run", run.Id.ToString(), ct: ct);

        return Ok(new
        {
            run,
            payrolls = generated,
            skipped = run.Skipped,
            total = teachers.Count,
        });
    }

    [HttpPost("generate/teacher")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> GenerateTeacher([FromBody] GenerateTeacherRequest req, CancellationToken ct)
    {
        var s = await _db.SalaryStructures.AsNoTracking().FirstOrDefaultAsync(x => x.TeacherId == req.TeacherId && x.IsActive, ct);
        if (s is null) return BadRequest(new { error = "No active salary structure for this teacher" });
        var teacher = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.TeacherId, ct);
        if (teacher is null) return NotFound(new { error = "Teacher not found" });

        var existing = await _db.Payrolls.FirstOrDefaultAsync(
            p => p.TeacherId == req.TeacherId && p.Year == req.Year && p.Month == req.Month, ct);
        if (existing is not null)
        {
            existing.GuardRegeneration(force: false);   // 409 if locked/paid
            _db.Payrolls.Remove(existing);
        }

        var unpaid = await ComputeUnpaidDaysAsync(req.TeacherId, req.Year, req.Month, ct);
        var payslip = Domain.Entities.Payroll.BuildPayslip(s, teacher, req.Month, req.Year, req.AcademicYear, unpaid);
        _db.Payrolls.Add(payslip);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("payroll.generate_teacher", "payroll", payslip.Id.ToString(), ct: ct);
        return StatusCode(201, payslip);
    }

    [HttpGet]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> ListPayrolls([FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? limit, CancellationToken ct)
    {
        var q = _db.Payrolls.AsNoTracking().AsQueryable();
        if (year is { } y) q = q.Where(p => p.Year == y);
        if (month is { } m) q = q.Where(p => p.Month == m);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PayrollStatus>(status, true, out var st)) q = q.Where(p => p.Status == st);
        var pg = Math.Max(1, page ?? 1); var l = Math.Clamp(limit ?? PageInfo.DefaultLimit, 1, 100);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Skip((pg - 1) * l).Take(l).ToListAsync(ct);
        return Ok(new Paged<Domain.Entities.Payroll> { Items = items, Pagination = PageInfo.Create(total, pg, l) });
    }

    [HttpGet("{id:guid}")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> GetPayroll(Guid id, CancellationToken ct)
    {
        var p = await _db.Payrolls.FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? NotFound(new { error = "Not found" }) : Ok(p);
    }

    [HttpGet("teacher/{teacherId:guid}")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> TeacherPayslips(Guid teacherId, CancellationToken ct)
    {
        var items = await _db.Payrolls.AsNoTracking().Where(p => p.TeacherId == teacherId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync(ct);
        return Ok(new { items, count = items.Count });
    }

    [HttpPost("{id:guid}/lock")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct) => await Transition(id, PayrollStatus.Locked, ct);

    [HttpPost("{id:guid}/unlock")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct) => await Transition(id, PayrollStatus.Generated, ct);

    private async Task<IActionResult> Transition(Guid id, PayrollStatus to, CancellationToken ct)
    {
        var p = await _db.Payrolls.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });
        // Status-only change — the DB trigger permits locked→generated / locked→paid,
        // and blocks data edits of locked/paid rows.
        if (to == PayrollStatus.Locked) { p.LockedAt = DateTime.UtcNow; p.LockedByUserId = _tenant.UserId; }
        else { p.LockedAt = null; p.LockedByUserId = null; }
        p.Status = to;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync($"payroll.{(to == PayrollStatus.Locked ? "lock" : "unlock")}", "payroll", id.ToString(), ct: ct);
        return Ok(p);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await _db.Payrolls.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });
        // Trigger blocks delete of locked/paid; surface a clean 409 first.
        if (p.Status is PayrollStatus.Locked or PayrollStatus.Paid)
            return Conflict(new { error = $"Payslip is {p.Status.ToString().ToLowerInvariant()} and cannot be deleted", code = ErrorCodes.DuplicateError });
        _db.Payrolls.Remove(p);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("payroll.delete", "payroll", id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }

    [HttpGet("runs")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> ListRuns(CancellationToken ct) =>
        Ok(new { items = await _db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync(ct) });

    // ── Leave types (stored on the school's leave policy) ─────────────────
    private static readonly object[] DefaultLeaveTypes =
    {
        new { name = "Sick",      totalDays = 12m,  isPaid = true,  color = "#EF4444", description = "Medical / illness" },
        new { name = "Casual",    totalDays = 12m,  isPaid = true,  color = "#3B82F6", description = "Personal / short notice" },
        new { name = "Earned",    totalDays = 15m,  isPaid = true,  color = "#10B981", description = "Accrued / vacation" },
        new { name = "Maternity", totalDays = 180m, isPaid = true,  color = "#EC4899", description = "As per Maternity Benefit Act" },
        new { name = "Unpaid",    totalDays = 0m,   isPaid = false, color = "#6B7280", description = "Loss of pay" },
    };

    [HttpGet("leave-types")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> ListLeaveTypes(CancellationToken ct)
    {
        var school = await _db.Schools.AsNoTracking().IgnoreQueryFilters()
            .Include(s => s.LeaveTypes)
            .FirstOrDefaultAsync(s => s.Id == _tenant.SchoolId, ct);

        var configured = school?.LeaveTypes;
        var hasCustom = configured is { Count: > 0 };

        return Ok(new
        {
            types = hasCustom
                ? configured!.Select(t => new { name = t.Name, totalDays = t.TotalDays, isPaid = t.IsPaid, color = t.Color, description = t.Description }).ToArray<object>()
                : DefaultLeaveTypes,
            isDefault = !hasCustom,
            requireApproval = school?.LeaveRequireApproval ?? true,
        });
    }

    public sealed record LeaveTypeInput(string Name, decimal TotalDays, bool? IsPaid, string? Color, string? Description);
    public sealed record UpdateLeaveTypesRequest(List<LeaveTypeInput>? Types, bool? RequireApproval);

    [HttpPut("leave-types")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> UpdateLeaveTypes([FromBody] UpdateLeaveTypesRequest req, CancellationToken ct)
    {
        if (req.Types is null || req.Types.Count == 0)
            return BadRequest(new { error = "types[] required" });
        foreach (var t in req.Types)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) return BadRequest(new { error = "Each type needs a name" });
            if (t.TotalDays < 0) return BadRequest(new { error = $"Invalid totalDays for {t.Name}" });
        }

        var school = await _db.Schools.IgnoreQueryFilters()
            .Include(s => s.LeaveTypes)
            .FirstOrDefaultAsync(s => s.Id == _tenant.SchoolId, ct);
        if (school is null) return NotFound(new { error = "School not found" });

        _db.RemoveRange(school.LeaveTypes);
        school.LeaveTypes = req.Types.Select(t => new SchoolLeaveType
        {
            SchoolId = school.Id, Name = t.Name.Trim(), TotalDays = t.TotalDays,
            IsPaid = t.IsPaid != false, Color = t.Color, Description = t.Description,
        }).ToList();
        if (req.RequireApproval is { } ra) school.LeaveRequireApproval = ra;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("leave_types.update", "school", school.Id.ToString(), ct: ct);

        return Ok(new
        {
            types = school.LeaveTypes.Select(t => new { name = t.Name, totalDays = t.TotalDays, isPaid = t.IsPaid, color = t.Color, description = t.Description }),
            requireApproval = school.LeaveRequireApproval,
        });
    }

    // AI leave-text parsing: the Claude path needs ANTHROPIC_API_KEY; without it
    // a heuristic fallback extracts type + rough dates so the UI still works.
    public sealed record ParseLeaveRequest(string? Text);
    [HttpPost("leaves/parse")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> ParseLeave([FromBody] ParseLeaveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "text required" });

        var school = await _db.Schools.AsNoTracking().IgnoreQueryFilters()
            .Include(s => s.LeaveTypes).FirstOrDefaultAsync(s => s.Id == _tenant.SchoolId, ct);
        var typeNames = school is { LeaveTypes.Count: > 0 }
            ? school.LeaveTypes.Select(t => t.Name).ToArray()
            : new[] { "Sick", "Casual", "Earned", "Maternity", "Unpaid" };

        // Heuristic: match a known type name, default a 1-day leave today.
        var text = req.Text.Trim();
        var matched = typeNames.FirstOrDefault(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)) ?? typeNames[0];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return Ok(new
        {
            parsed = new { type = matched, fromDate = today, toDate = today, days = 1m, reason = text, aiUsed = false },
            source = "heuristic",
            text,
        });
    }

    [HttpDelete("leaves/record")]
    [RequirePrivilege("payroll:manage")]
    public async Task<IActionResult> DeleteLeaveRecord(
        [FromQuery] Guid teacherId, [FromQuery] Guid leaveId, [FromQuery] string? academicYear, CancellationToken ct)
    {
        if (teacherId == Guid.Empty || leaveId == Guid.Empty)
            return BadRequest(new { error = "teacherId, leaveId required" });

        var rec = await _db.LeaveRecords.Include(r => r.Leave)
            .FirstOrDefaultAsync(r => r.Id == leaveId && r.Leave.TeacherId == teacherId, ct);
        if (rec is null) return NotFound(new { error = "Leave entry not found" });

        _db.LeaveRecords.Remove(rec);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("leave.delete", "leave", leaveId.ToString(), ct: ct);
        return Ok(new { ok = true });
    }

    private async Task<decimal> ComputeUnpaidDaysAsync(Guid teacherId, int year, int month, CancellationToken ct)
    {
        var leave = await _db.Leaves.AsNoTracking().Include(l => l.Types).Include(l => l.Records)
            .FirstOrDefaultAsync(l => l.TeacherId == teacherId, ct);
        var fromLeave = leave?.UnpaidLeaveDaysInMonth(year, month) ?? 0m;

        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var statuses = await _db.TeacherAttendance.AsNoTracking()
            .Where(a => a.TeacherId == teacherId && a.Date >= start && a.Date <= end)
            .Select(a => a.Status).ToListAsync(ct);
        var fromAttendance = TeacherAttendanceRules.UnpaidDays(statuses);

        return Math.Max(fromLeave, fromAttendance);   // whichever source records more
    }
}
