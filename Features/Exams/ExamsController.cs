using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Exams;

[ApiController]
[Route("api/exams")]
public sealed class ExamsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public ExamsController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> List([FromQuery] string? academicYear, [FromQuery] string? @class, CancellationToken ct)
    {
        var q = _db.Exams.AsNoTracking().Include(e => e.Subjects).AsQueryable();
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(e => e.AcademicYear == academicYear);
        if (!string.IsNullOrEmpty(@class)) q = q.Where(e => e.Class == @class);
        var items = await q.OrderByDescending(e => e.FromDate).ToListAsync(ct);
        return Ok(new { items, count = items.Count });
    }

    [HttpGet("{id:guid}")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var e = await _db.Exams.Include(x => x.Subjects).FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? NotFound(new { error = "Not found" }) : Ok(e);
    }

    [HttpPost]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> Create([FromBody] ExamWriteRequest req, CancellationToken ct)
    {
        var missing = new List<object>();
        if (string.IsNullOrWhiteSpace(req.Name))  missing.Add(new { field = "name",  message = "Exam name is required." });
        if (string.IsNullOrWhiteSpace(req.Class)) missing.Add(new { field = "class", message = "Class is required." });

        if (!EnumWireParse.TryOptional<ExamType>(req.Type, out var examType))
            missing.Add(new { field = "type", message = $"Must be one of: {EnumWireParse.Allowed<ExamType>()}." });
        else if (examType is null)
            missing.Add(new { field = "type", message = "Exam type is required." });

        if (!EnumWireParse.TryOptional<ExamStatus>(req.Status, out var examStatus))
            missing.Add(new { field = "status", message = $"Must be one of: {EnumWireParse.Allowed<ExamStatus>()}." });

        if (missing.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = missing });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var body = new Exam
        {
            Name = req.Name!.Trim(),
            Type = examType!.Value,
            Class = req.Class!.Trim(),
            Section = string.IsNullOrWhiteSpace(req.Section) ? null : req.Section.Trim(),
            FromDate = req.FromDate ?? today,
            ToDate = req.ToDate ?? req.FromDate ?? today,

            // Omitted by the mobile create form — 0 means "no rollup weight",
            // which is the correct default, not a validation error.
            WeightInFinal = req.WeightInFinal ?? 0m,

            GradingScaleId = req.GradingScaleId,
            Notes = req.Notes,
            Status = examStatus ?? ExamStatus.Draft,
        };

        body.AcademicYear = await ResolveAcademicYearAsync(req.AcademicYear, ct);

        foreach (var sub in req.Subjects ?? [])
        {
            if (sub.SubjectId is not { } sid) continue;
            body.Subjects.Add(new ExamSubject
            {
                SubjectId = sid,
                SubjectName = sub.SubjectName ?? "",
                MaxMarks = sub.MaxMarks ?? 100m,
                PassingMark = sub.PassingMark,
                TheoryMax = sub.TheoryMax,
                PracticalMax = sub.PracticalMax,
                ExamDate = sub.ExamDate,
                StartTime = sub.StartTime,
                DurationMins = sub.DurationMins,
            });
        }

        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.Exams.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("exam.create", "exam", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ExamWriteRequest req, CancellationToken ct)
    {
        var e = await _db.Exams.Include(x => x.Subjects).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { error = "Not found" });

        var bad = new List<object>();
        if (!EnumWireParse.TryOptional<ExamType>(req.Type, out var t))
            bad.Add(new { field = "type", message = $"Must be one of: {EnumWireParse.Allowed<ExamType>()}." });
        if (!EnumWireParse.TryOptional<ExamStatus>(req.Status, out var newStatus))
            bad.Add(new { field = "status", message = $"Must be one of: {EnumWireParse.Allowed<ExamStatus>()}." });
        if (bad.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = bad });

        // Meta only — subjects are create-time, unchanged from before.
        if (!string.IsNullOrWhiteSpace(req.Name)) e.Name = req.Name.Trim();
        if (t is { } tv) e.Type = tv;
        if (req.FromDate is { } fd) e.FromDate = fd;
        if (req.ToDate is { } td) e.ToDate = td;
        if (req.WeightInFinal is { } w) e.WeightInFinal = w;
        if (newStatus is { } st) e.Status = st;
        e.GradingScaleId = req.GradingScaleId;
        e.Section = string.IsNullOrWhiteSpace(req.Section) ? null : req.Section.Trim();
        e.Notes = req.Notes;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("exam.update", "exam", id.ToString(), ct: ct);
        return Ok(e);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("exam:create")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var e = await _db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { error = "Not found" });
        _db.Exams.Remove(e);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("exam.delete", "exam", id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/publish")]
    [RequirePrivilege("exam:publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => await SetStatus(id, ExamStatus.Published, ct);

    [HttpPost("{id:guid}/unpublish")]
    [RequirePrivilege("exam:publish")]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct) => await SetStatus(id, ExamStatus.Completed, ct);

    private async Task<IActionResult> SetStatus(Guid id, ExamStatus status, CancellationToken ct)
    {
        var e = await _db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound(new { error = "Not found" });
        e.Status = status;
        e.PublishedAt = status == ExamStatus.Published ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync($"exam.{(status == ExamStatus.Published ? "publish" : "unpublish")}", "exam", id.ToString(), ct: ct);
        return Ok(e);
    }

    // GET marksheet — students × subjects grid with existing results.
    [HttpGet("{id:guid}/marksheet")]
    [RequirePrivilege("exam:grade")]
    public async Task<IActionResult> GetMarksheet(Guid id, [FromQuery] string? section, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking().Include(e => e.Subjects).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return NotFound(new { error = "Not found" });

        var studentsQ = _db.Students.AsNoTracking().Where(s => s.Class == exam.Class && s.Status == StudentStatus.Active);
        if (!string.IsNullOrEmpty(section)) studentsQ = studentsQ.Where(s => s.Section == section);
        else if (!string.IsNullOrEmpty(exam.Section)) studentsQ = studentsQ.Where(s => s.Section == exam.Section);
        var students = await studentsQ.OrderBy(s => s.RollNo).ToListAsync(ct);

        var results = await _db.ExamResults.AsNoTracking().Where(r => r.ExamId == id).ToListAsync(ct);
        var byKey = results.ToDictionary(r => (r.StudentId, r.SubjectId));

        var grid = students.Select(s => new
        {
            student = new { _id = s.Id, s.AdmissionNo, s.RollNo, s.FirstName, s.LastName },
            marks = exam.Subjects.Select(sub => new
            {
                subjectId = sub.SubjectId, sub.SubjectName, sub.MaxMarks,
                result = byKey.GetValueOrDefault((s.Id, sub.SubjectId)),
            }),
        });
        return Ok(new { exam, students = grid });
    }

    // POST marksheet/save — upsert per cell; absent ⇒ marks NULL, then Recompute.
    public sealed record MarkCell(Guid StudentId, Guid SubjectId, decimal? MarksObtained,
                                  decimal MaxMarks, string Status = "present", string? Remarks = null);
    public sealed record SaveMarksheetRequest(List<MarkCell>? Cells);

    [HttpPost("{id:guid}/marksheet/save")]
    [RequirePrivilege("exam:grade")]
    public async Task<IActionResult> SaveMarksheet(Guid id, [FromBody] SaveMarksheetRequest req, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking().Include(e => e.Subjects).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return NotFound(new { error = "Not found" });
        if (req.Cells is null || req.Cells.Count == 0) return BadRequest(new { error = "cells array required" });

        var scale = exam.GradingScaleId is { } gsid
            ? await _db.GradingScales.AsNoTracking().Include(g => g.Bands).FirstOrDefaultAsync(g => g.Id == gsid, ct)
            : null;

        var subjectNames = exam.Subjects.ToDictionary(s => s.SubjectId, s => s.SubjectName);
        int saved = 0;
        await Tx.RunAsync(_db, async () =>
        {
            foreach (var c in req.Cells)
            {
                if (!Enum.TryParse<ExamResultStatus>(c.Status, true, out var st)) st = ExamResultStatus.Present;
                var r = await _db.ExamResults.FirstOrDefaultAsync(
                    x => x.ExamId == id && x.StudentId == c.StudentId && x.SubjectId == c.SubjectId, ct);
                if (r is null)
                {
                    r = new ExamResult
                    {
                        SchoolId = _tenant.SchoolId ?? Guid.Empty, ExamId = id,
                        StudentId = c.StudentId, SubjectId = c.SubjectId,
                        ExamName = exam.Name, SubjectName = subjectNames.GetValueOrDefault(c.SubjectId),
                        AcademicYear = exam.AcademicYear, Class = exam.Class,
                        MaxMarks = c.MaxMarks, EnteredByUserId = _tenant.UserId,
                    };
                    _db.ExamResults.Add(r);
                }
                r.MarksObtained = c.MarksObtained;   // absent path nulls it in Recompute
                r.MaxMarks = c.MaxMarks;
                r.Status = st;
                r.Remarks = c.Remarks;
                r.Recompute(scale);                  // percentage/grade/isPassing, absent-safe
                saved++;
            }
            await _db.SaveChangesAsync(ct);
        }, ct);

        await _audit.WriteAsync("exam.marksheet_save", "exam", id.ToString(),
            metaJson: System.Text.Json.JsonSerializer.Serialize(new { saved }), ct: ct);
        return Ok(new { saved });
    }

    [HttpGet("{id:guid}/results")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> Results(Guid id, CancellationToken ct)
    {
        var items = await _db.ExamResults.AsNoTracking().Where(r => r.ExamId == id).ToListAsync(ct);
        return Ok(new { items, count = items.Count });
    }

    /// <summary>Falls back to the school's current academic year when the
    /// client omits it — the mobile create form does not send it.</summary>
    private async Task<string> ResolveAcademicYearAsync(string? bodyValue, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(bodyValue)) return bodyValue.Trim();

        var ay = await _db.Schools.AsNoTracking()
            .Where(x => x.Id == _tenant.SchoolId)
            .Select(x => x.AcademicYear)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(ay)) return ay;

        var y = DateTime.UtcNow.Year;
        return $"{y}-{y + 1}";
    }
}
