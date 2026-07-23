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

    /// <summary>
    /// Full exam analytics: per-student matrix, 1224 ranking, division bands,
    /// subject toppers, class topper and aggregate stats.
    ///
    /// The Node original returned all of this. The port reduced it to a flat
    /// dump of ExamResult rows (`{ items, count }`), which is why the exams
    /// screen could only show "Results entered: N" — same URL and privilege,
    /// silently different contract. This restores the original shape.
    ///
    /// Everything is computed in memory after two queries: the ranking, the
    /// per-subject topper scan and the division bands are not expressible in
    /// SQL cheaply, and a class is tens of rows, not thousands.
    /// </summary>
    [HttpGet("{id:guid}/results")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> Results(Guid id, CancellationToken ct)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Include(e => e.Subjects)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null) return NotFound(new { error = "Not found" });

        var scale = exam.GradingScaleId is null ? null
            : await _db.GradingScales.AsNoTracking().Include(s => s.Bands)
                .FirstOrDefaultAsync(s => s.Id == exam.GradingScaleId, ct);

        var results = await _db.ExamResults.AsNoTracking()
            .Where(r => r.ExamId == id).ToListAsync(ct);

        // Roster = every active student in the exam's class/section, so students
        // with no marks entered still appear (as anyMissing) rather than
        // vanishing from the report.
        var roster = await _db.Students.AsNoTracking()
            .Where(s => s.Class == exam.Class
                     && (exam.Section == null || s.Section == exam.Section)
                     && s.Status == StudentStatus.Active)
            .OrderBy(s => s.RollNo).ThenBy(s => s.FirstName)
            .ToListAsync(ct);

        var subjects = exam.Subjects.ToList();
        var byStudent = results.GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.SubjectId, x => x));

        var matrix = new List<ResultRow>();
        foreach (var s in roster)
        {
            byStudent.TryGetValue(s.Id, out var mine);
            decimal totalObtained = 0, totalMax = 0;
            bool anyAbsent = false, anyMissing = false, anyFail = false;
            var subjectRows = new List<object?>();

            foreach (var sub in subjects)
            {
                ExamResult? r = null;
                mine?.TryGetValue(sub.SubjectId, out r);

                if (r is null) { anyMissing = true; subjectRows.Add(null); continue; }

                if (r.Status == ExamResultStatus.Absent)
                {
                    anyAbsent = true; anyFail = true;
                }
                else if (r.MarksObtained is not null)
                {
                    totalObtained += r.MarksObtained.Value;
                    totalMax += sub.MaxMarks;
                    // Per-subject passing override, else the Indian 33% default.
                    var passMark = sub.PassingMark ?? (sub.MaxMarks * 0.33m);
                    if (r.MarksObtained.Value < passMark) anyFail = true;
                }

                subjectRows.Add(new
                {
                    subjectId = sub.SubjectId,
                    subjectName = sub.SubjectName,
                    maxMarks = sub.MaxMarks,
                    marksObtained = r.MarksObtained,
                    percentage = r.Percentage,
                    grade = r.Grade,
                    gpa = r.Gpa,
                    status = EnumWireParse.ToWire(r.Status),
                    isPassing = r.IsPassing,
                });
            }

            var overallPct = totalMax > 0
                ? Math.Round((totalObtained / totalMax) * 100m, 1) : 0m;
            var overallGrade = scale is not null && totalMax > 0
                ? scale.GradeFor(overallPct).Grade : null;

            // Indian-board division bands — common across CBSE, ICSE and state
            // boards. Only meaningful when the student sat every paper.
            string? division = null;
            if (totalMax > 0 && !anyAbsent && !anyMissing)
            {
                if (anyFail) division = "Fail";
                else if (overallPct >= 60) division = "First";
                else if (overallPct >= 45) division = "Second";
                else if (overallPct >= 33) division = "Third";
                else division = "Fail";
            }

            matrix.Add(new ResultRow
            {
                StudentId = s.Id,
                AdmissionNo = s.AdmissionNo,
                RollNo = s.RollNo,
                Name = string.Join(" ", new[] { s.FirstName, s.LastName }
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
                Subjects = subjectRows,
                TotalObtained = totalObtained,
                TotalMax = totalMax,
                OverallPct = overallPct,
                OverallGrade = overallGrade,
                Division = division,
                AnyAbsent = anyAbsent,
                AnyMissing = anyMissing,
                AnyFail = anyFail,
                IsPassing = !anyFail && !anyMissing && totalMax > 0,
            });
        }

        // 1224 ranking (competitive standard): equal percentages share a rank
        // and the next distinct percentage skips ahead. Students with any
        // subject unmarked are excluded rather than ranked last — an incomplete
        // marksheet is not a poor performance.
        var rankable = matrix.Where(r => !r.AnyMissing && r.TotalMax > 0)
            .OrderByDescending(r => r.OverallPct).ToList();
        decimal? lastPct = null; int lastRank = 0;
        for (var i = 0; i < rankable.Count; i++)
        {
            if (lastPct is not null && rankable[i].OverallPct == lastPct.Value)
            {
                rankable[i].Rank = lastRank;
            }
            else
            {
                rankable[i].Rank = i + 1;
                lastRank = i + 1;
                lastPct = rankable[i].OverallPct;
            }
        }

        // Highest scorer per subject, present students only.
        var subjectToppers = subjects.Select(sub =>
        {
            object? top = null;
            decimal best = decimal.MinValue;
            foreach (var row in matrix)
            {
                if (!byStudent.TryGetValue(row.StudentId, out var mine)) continue;
                if (!mine.TryGetValue(sub.SubjectId, out var r)) continue;
                if (r.Status != ExamResultStatus.Present || r.MarksObtained is null) continue;
                if (r.MarksObtained.Value > best)
                {
                    best = r.MarksObtained.Value;
                    top = new
                    {
                        studentId = row.StudentId,
                        name = row.Name,
                        rollNo = row.RollNo,
                        marksObtained = r.MarksObtained,
                        maxMarks = sub.MaxMarks,
                        percentage = r.Percentage,
                    };
                }
            }
            return new { subjectId = sub.SubjectId, subjectName = sub.SubjectName, topper = top };
        }).ToList();

        var presentCount = matrix.Count(r => !r.AnyMissing && !r.AnyAbsent);
        var passCount = matrix.Count(r => r.IsPassing);
        var failCount = matrix.Count(r => !r.IsPassing && !r.AnyMissing);
        var absentCount = matrix.Count(r => r.AnyAbsent);
        var pcts = rankable.Select(r => r.OverallPct).ToList();

        var divisions = new Dictionary<string, int>
        { ["First"] = 0, ["Second"] = 0, ["Third"] = 0, ["Fail"] = 0 };
        foreach (var r in matrix)
            if (r.Division is not null && divisions.ContainsKey(r.Division)) divisions[r.Division]++;

        var topRow = rankable.FirstOrDefault();

        return Ok(new
        {
            exam = new
            {
                _id = exam.Id, name = exam.Name,
                type = EnumWireParse.ToWire(exam.Type),
                status = EnumWireParse.ToWire(exam.Status),
                fromDate = exam.FromDate, toDate = exam.ToDate,
                @class = exam.Class, section = exam.Section,
                academicYear = exam.AcademicYear,
            },
            gradingScale = scale is null ? null : new
            {
                _id = scale.Id, name = scale.Name,
                type = EnumWireParse.ToWire(scale.Type),
            },
            subjects,
            rows = matrix.Select(r => new
            {
                student = new { _id = r.StudentId, admissionNo = r.AdmissionNo, rollNo = r.RollNo, name = r.Name },
                subjects = r.Subjects,
                totalObtained = r.TotalObtained, totalMax = r.TotalMax,
                overallPct = r.OverallPct, overallGrade = r.OverallGrade,
                division = r.Division, rank = r.Rank,
                anyAbsent = r.AnyAbsent, anyMissing = r.AnyMissing,
                anyFail = r.AnyFail, isPassing = r.IsPassing,
            }),
            stats = new
            {
                totalStudents = matrix.Count,
                presentCount, passCount, failCount, absentCount,
                passPercentage = presentCount > 0
                    ? Math.Round((decimal)passCount / presentCount * 100m, 1) : 0m,
                avgPct = pcts.Count > 0 ? Math.Round(pcts.Average(), 1) : 0m,
                highPct = pcts.Count > 0 ? pcts.Max() : 0m,
                lowPct = pcts.Count > 0 ? pcts.Min() : 0m,
                divisions,
            },
            subjectToppers,
            classTopper = topRow is null ? null : new
            {
                studentId = topRow.StudentId,
                name = topRow.Name,
                rollNo = topRow.RollNo,
                percentage = topRow.OverallPct,
                grade = topRow.OverallGrade,
            },
        });
    }

    /// <summary>Mutable carrier so rank can be assigned after sorting.</summary>
    private sealed class ResultRow
    {
        public Guid StudentId { get; init; }
        public string AdmissionNo { get; init; } = "";
        public string? RollNo { get; init; }
        public string Name { get; init; } = "";
        public List<object?> Subjects { get; init; } = [];
        public decimal TotalObtained { get; init; }
        public decimal TotalMax { get; init; }
        public decimal OverallPct { get; init; }
        public string? OverallGrade { get; init; }
        public string? Division { get; init; }
        public bool AnyAbsent { get; init; }
        public bool AnyMissing { get; init; }
        public bool AnyFail { get; init; }
        public bool IsPassing { get; init; }
        public int? Rank { get; set; }
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
