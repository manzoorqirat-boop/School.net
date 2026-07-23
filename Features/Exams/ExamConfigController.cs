using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Exams;

[ApiController]
[Route("api/exam-config")]
public sealed class ExamConfigController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public ExamConfigController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    // ── Grading scales ────────────────────────────────────────────────────
    [HttpGet("scales")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> ListScales(CancellationToken ct) =>
        Ok(new { items = await _db.GradingScales.AsNoTracking().Include(s => s.Bands).ToListAsync(ct) });

    [HttpPost("scales")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> CreateScale([FromBody] GradingScale body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.GradingScales.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("grading_scale.create", "grading_scale", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("scales/{id:guid}")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> UpdateScale(Guid id, [FromBody] GradingScale body, CancellationToken ct)
    {
        var s = await _db.GradingScales.Include(x => x.Bands).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        s.Name = body.Name; s.Type = body.Type; s.PassingMark = body.PassingMark;
        s.IsDefault = body.IsDefault; s.IsActive = body.IsActive;

        // Same trap as TimetableController.SaveEntries: RemoveRange marks the
        // old bands Deleted, then the incoming collection is attached as Added.
        // If a client echoes back the bands it just read — which any edit form
        // naturally does — their Ids are already tracked and EF throws, giving
        // a 500 on what looks like an ordinary save. Incoming bands are new
        // rows here, so drop any client-supplied Id and let the store assign.
        _db.GradeBands.RemoveRange(s.Bands);

        var incoming = body.Bands ?? [];
        foreach (var b in incoming)
        {
            b.Id = Guid.Empty;
            b.GradingScaleId = s.Id;
            b.GradingScale = null!;
        }

        s.Bands = incoming;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("grading_scale.update", "grading_scale", id.ToString(), ct: ct);
        return Ok(s);
    }

    [HttpDelete("scales/{id:guid}")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> DeleteScale(Guid id, CancellationToken ct)
    {
        var s = await _db.GradingScales.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        _db.GradingScales.Remove(s);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    // ── Subjects ──────────────────────────────────────────────────────────
    [HttpGet("subjects")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> ListSubjects([FromQuery] string? @class, [FromQuery] string? academicYear, CancellationToken ct)
    {
        var q = _db.Subjects.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(@class)) q = q.Where(s => s.Class == @class);
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(s => s.AcademicYear == academicYear);
        return Ok(new { items = await q.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToListAsync(ct) });
    }

    [HttpPost("subjects")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> CreateSubject([FromBody] Subject body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.Subjects.Add(body);
        await _db.SaveChangesAsync(ct);
        return StatusCode(201, body);
    }

    [HttpPut("subjects/{id:guid}")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> UpdateSubject(Guid id, [FromBody] Subject body, CancellationToken ct)
    {
        var s = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        s.Name = body.Name; s.Code = body.Code; s.IsCoScholastic = body.IsCoScholastic;
        s.DefaultMaxMarks = body.DefaultMaxMarks; s.DisplayOrder = body.DisplayOrder; s.IsActive = body.IsActive;

        // Class and AcademicYear were previously NOT copied, so a subject could
        // never be moved between classes or carried into a new year — the PUT
        // returned 200 and silently discarded those two fields. They scope the
        // whole record (subjects are listed per class + year), so a client that
        // sends them expects them applied. Guard against blanks so a partial
        // payload cannot orphan a subject out of its class.
        if (!string.IsNullOrWhiteSpace(body.Class)) s.Class = body.Class;
        if (!string.IsNullOrWhiteSpace(body.AcademicYear)) s.AcademicYear = body.AcademicYear;

        await _db.SaveChangesAsync(ct);
        return Ok(s);
    }

    [HttpDelete("subjects/{id:guid}")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> DeleteSubject(Guid id, CancellationToken ct)
    {
        var s = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        _db.Subjects.Remove(s);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    public sealed record BulkSubjectsRequest(List<Subject>? Subjects);
    [HttpPost("subjects/bulk")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> BulkSubjects([FromBody] BulkSubjectsRequest req, CancellationToken ct)
    {
        if (req.Subjects is null || req.Subjects.Count == 0) return BadRequest(new { error = "subjects array required" });
        foreach (var s in req.Subjects) { s.SchoolId = _tenant.SchoolId ?? Guid.Empty; _db.Subjects.Add(s); }
        await _db.SaveChangesAsync(ct);
        return Ok(new { created = req.Subjects.Count });
    }
}

[ApiController]
[Route("api/report-cards")]
public sealed class ReportCardsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    public ReportCardsController(AppDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

    // Aggregate a student's results across all exams in a year into a report card.
    [HttpGet("student/{studentId:guid}")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> StudentReportCard(Guid studentId, [FromQuery] string? academicYear, CancellationToken ct)
    {
        // Row-scope: parent/student can only see their own.
        if (_tenant.Role == "parent")
        {
            var owned = await _db.Users.IgnoreQueryFilters().Where(u => u.Id == _tenant.UserId)
                .SelectMany(u => u.ParentOf.Select(s => s.Id)).ToListAsync(ct);
            if (!owned.Contains(studentId)) return StatusCode(403, new { error = "Forbidden" });
        }
        if (_tenant.Role == "student" && _tenant.StudentId != studentId)
            return StatusCode(403, new { error = "Forbidden" });

        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound(new { error = "Not found" });

        var q = _db.ExamResults.AsNoTracking().Where(r => r.StudentId == studentId);
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(r => r.AcademicYear == academicYear);
        var results = await q.ToListAsync(ct);

        var byExam = results.GroupBy(r => new { r.ExamId, r.ExamName }).Select(g =>
        {
            var graded = g.Where(r => r.Status == ExamResultStatus.Present && r.MarksObtained != null).ToList();
            var obtained = graded.Sum(r => r.MarksObtained!.Value);
            var max = graded.Sum(r => r.MaxMarks);
            return new
            {
                examId = g.Key.ExamId, examName = g.Key.ExamName,
                subjects = g.Select(r => new { r.SubjectName, r.MarksObtained, r.MaxMarks, r.Percentage, r.Grade, r.IsPassing, r.Status }),
                totalObtained = obtained, totalMax = max,
                percentage = max > 0 ? Math.Round(obtained * 100m / max, 1, MidpointRounding.AwayFromZero) : 0m,
            };
        });

        return Ok(new
        {
            student = new { _id = student.Id, student.AdmissionNo, student.RollNo, student.FirstName, student.LastName, student.Class, student.Section },
            academicYear,
            exams = byExam,
        });
    }
}
