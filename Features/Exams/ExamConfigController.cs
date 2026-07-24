using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Features.Documents;
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

    /// <summary>
    /// Aggregate a student's results across a year into a report card.
    ///
    /// The port had reduced this to student + a per-exam total. Restored to the
    /// Node contract, because the PDF/print layer needs all of it:
    ///   • school block   — name, address, logo (report card letterhead)
    ///   • parent names + dob (the student detail block)
    ///   • exam type / dates / status / weightInFinal
    ///   • grading scale and overallGrade per exam
    ///   • composite      — weighted year-final across exams with weightInFinal
    ///
    /// It also walks the EXAM's subject list rather than grouping the result
    /// rows: grouping meant a subject with no marks entered vanished from the
    /// card entirely instead of showing as not_entered.
    /// </summary>
    [HttpGet("student/{studentId:guid}")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> StudentReportCard(Guid studentId, [FromQuery] string? academicYear, [FromQuery] Guid? examId, CancellationToken ct)
    {
        var guard = await AuthoriseAsync(studentId, ct);
        if (guard is not null) return guard;

        var built = await BuildAsync(studentId, academicYear, examId, ct);
        if (built is null) return NotFound(new { error = "Not found" });
        return Ok(built.Payload);
    }

    /// <summary>
    /// The same card as a PDF. Restores GET /api/report-cards/student/:id/pdf
    /// from the Node backend — the port dropped the endpoint entirely, so
    /// there was no printable report card at all.
    /// </summary>
    [HttpGet("student/{studentId:guid}/pdf")]
    [RequirePrivilege("exam:view")]
    public async Task<IActionResult> StudentReportCardPdf(Guid studentId, [FromQuery] string? academicYear, [FromQuery] Guid? examId, CancellationToken ct)
    {
        var guard = await AuthoriseAsync(studentId, ct);
        if (guard is not null) return guard;

        var b = await BuildAsync(studentId, academicYear, examId, ct);
        if (b is null) return NotFound(new { error = "Not found" });

        var st = b.Student;
        var addr = string.Join(", ", new[] { b.School?.Address, b.School?.City, b.School?.State, b.School?.Pincode }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        var data = new ReportCardData(
            SchoolName: b.School?.Name ?? "School",
            SchoolAddressLine: addr.Length > 0 ? addr : null,
            StudentName: string.Join(" ", new[] { st.FirstName, st.LastName }
                .Where(x => !string.IsNullOrWhiteSpace(x))),
            AdmissionNo: st.AdmissionNo,
            ClassLabel: st.Class + (string.IsNullOrWhiteSpace(st.Section) ? "" : " - " + st.Section),
            RollNo: st.RollNo,
            FatherName: st.FatherName,
            MotherName: st.MotherName,
            AcademicYear: b.Year,
            Exams: b.Exams.Select(e => new RcExam(
                e.Exam.Name,
                EnumWireParse.ToWire(e.Exam.Type),
                e.Exam.FromDate, e.Exam.ToDate,
                e.Subjects.Select(x => new RcSubject(
                    x.SubjectName, x.MaxMarks, x.MarksObtained,
                    x.Percentage, x.Grade, x.Gpa, x.Status, x.IsPassing)).ToList(),
                e.TotalObtained, e.TotalMax, e.OverallPct, e.OverallGrade)).ToList(),
            Composite: b.Composite is null ? null : new RcComposite(
                b.CompositeWeights,
                b.Composite.Select(c => new RcCompositeSubject(c.SubjectName, c.FinalPercentage)).ToList(),
                b.CompositeOverall));

        var safe = new string((data.StudentName ?? "student")
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return File(ReportCardPdf.Build(data), "application/pdf", $"report-card-{safe}.pdf");
    }

    /// <summary>Row-scope guard shared by the JSON and PDF endpoints. Returns
    /// null when the caller is allowed through.</summary>
    private async Task<IActionResult?> AuthoriseAsync(Guid studentId, CancellationToken ct)
    {
        if (_tenant.Role == "parent")
        {
            var owned = await _db.Users.IgnoreQueryFilters().Where(u => u.Id == _tenant.UserId)
                .SelectMany(u => u.ParentOf.Select(s => s.Id)).ToListAsync(ct);
            if (!owned.Contains(studentId)) return StatusCode(403, new { error = "Forbidden" });
        }
        if (_tenant.Role == "student" && _tenant.StudentId != studentId)
            return StatusCode(403, new { error = "Forbidden" });
        return null;
    }

    /// <summary>
    /// Assembles the report card once. The JSON endpoint serialises
    /// <c>Payload</c>; the PDF endpoint reads the typed fields. Keeping one
    /// builder means the printed card can never drift from the on-screen one.
    /// </summary>
    private async Task<BuiltCard?> BuildAsync(Guid studentId, string? academicYear, Guid? examId, CancellationToken ct)
    {
        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return null;

        var year = string.IsNullOrWhiteSpace(academicYear) ? student.AcademicYear : academicYear;

        var examQ = _db.Exams.AsNoTracking().Include(e => e.Subjects)
            .Where(e => e.Class == student.Class && e.AcademicYear == year);
        if (examId is not null)
            examQ = examQ.Where(e => e.Id == examId.Value);
        else if (_tenant.Role is "parent" or "student")
            // Families see published exams only; staff see drafts too.
            examQ = examQ.Where(e => e.Status == ExamStatus.Published);

        var exams = await examQ.OrderBy(e => e.FromDate).ToListAsync(ct);

        var scaleIds = exams.Where(e => e.GradingScaleId != null)
            .Select(e => e.GradingScaleId!.Value).Distinct().ToList();
        var scales = await _db.GradingScales.AsNoTracking().Include(s => s.Bands)
            .Where(s => scaleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var examIds = exams.Select(e => e.Id).ToList();
        var allResults = await _db.ExamResults.AsNoTracking()
            .Where(r => r.StudentId == studentId && examIds.Contains(r.ExamId))
            .ToListAsync(ct);

        var examData = new List<ExamCard>();
        foreach (var exam in exams)
        {
            var mine = allResults.Where(r => r.ExamId == exam.Id)
                .ToDictionary(r => r.SubjectId, r => r);
            GradingScale? scale = null;
            if (exam.GradingScaleId is not null) scales.TryGetValue(exam.GradingScaleId.Value, out scale);

            var subjectRows = exam.Subjects.Select(sub =>
            {
                mine.TryGetValue(sub.SubjectId, out var r);
                return new SubjectRow
                {
                    SubjectId = sub.SubjectId,
                    SubjectName = sub.SubjectName,
                    MaxMarks = sub.MaxMarks,
                    MarksObtained = r?.MarksObtained,
                    Percentage = r?.Percentage,
                    Grade = r?.Grade,
                    Gpa = r?.Gpa,
                    Status = r is null ? "not_entered" : EnumWireParse.ToWire(r.Status),
                    IsPassing = r?.IsPassing,
                };
            }).ToList();

            var totalObtained = subjectRows.Sum(r => r.MarksObtained ?? 0m);
            var totalMax = subjectRows.Sum(r => r.MaxMarks);
            var overallPct = totalMax > 0 ? Math.Round(totalObtained * 100m / totalMax, 1) : 0m;

            examData.Add(new ExamCard
            {
                Exam = exam, Scale = scale, Subjects = subjectRows,
                TotalObtained = totalObtained, TotalMax = totalMax,
                OverallPct = overallPct,
                OverallGrade = scale?.GradeFor(overallPct).Grade,
            });
        }

        // Composite (year final): each exam contributes its weightInFinal share
        // of every subject's percentage. Only exams that declare a weight take
        // part, so a school not using weights simply gets composite = null.
        object? composite = null;
        // Hoisted so BuiltCard can hand the typed rows to the PDF builder
        // without recomputing them.
        List<CompositeSubject>? compositeTyped = null;
        decimal compositeWeights = 0;
        decimal compositeOverall = 0;
        var weighted = examData.Where(e => e.Exam.WeightInFinal > 0).ToList();
        if (weighted.Count > 0)
        {
            var sumWeights = weighted.Sum(e => e.Exam.WeightInFinal);
            var subjectIds = weighted.SelectMany(e => e.Subjects.Select(s => s.SubjectId)).Distinct();

            // Typed, not anonymous: the overall average below has to read
            // finalPercentage back, and `dynamic` inside a LINQ lambda fails at
            // runtime rather than compile time.
            var compositeSubjects = new List<CompositeSubject>();
            foreach (var sid in subjectIds)
            {
                decimal weightedSum = 0, weightApplied = 0;
                var subjectName = "";
                foreach (var e in weighted)
                {
                    var sub = e.Subjects.FirstOrDefault(s => s.SubjectId == sid);
                    if (sub?.Percentage is null) continue;
                    weightedSum += sub.Percentage.Value * e.Exam.WeightInFinal;
                    weightApplied += e.Exam.WeightInFinal;
                    subjectName = sub.SubjectName;
                }
                compositeSubjects.Add(new CompositeSubject(
                    sid, subjectName,
                    weightApplied > 0 ? Math.Round(weightedSum / weightApplied, 1) : 0m));
            }

            var overallFinal = compositeSubjects.Count > 0
                ? Math.Round(compositeSubjects.Sum(x => x.FinalPercentage) / compositeSubjects.Count, 1)
                : 0m;

            compositeTyped = compositeSubjects;
            compositeWeights = sumWeights;
            compositeOverall = overallFinal;

            composite = new
            {
                sumWeights,
                subjects = compositeSubjects.Select(x => new
                {
                    subjectId = x.SubjectId, subjectName = x.SubjectName,
                    finalPercentage = x.FinalPercentage,
                }),
                overallFinalPercentage = overallFinal,
            };
        }

        // Typed, not anonymous: the PDF builder needs to read these fields back,
        // and an anonymous type cannot cross a method boundary usefully.
        var school = await _db.Schools.AsNoTracking()
            .Where(s => s.Id == _tenant.SchoolId)
            .Select(s => new SchoolHeader(s.Name, s.NameHindi, s.Address, s.City, s.State, s.Pincode, s.LogoUrl, s.AcademicYear))
            .FirstOrDefaultAsync(ct);

        var payload = new
        {
            school = school is null ? null : new
            {
                name = school.Name, nameHindi = school.NameHindi,
                address = school.Address, city = school.City, state = school.State,
                pincode = school.Pincode, logoUrl = school.LogoUrl,
                academicYear = school.AcademicYear,
            },
            student = new
            {
                _id = student.Id, student.AdmissionNo, student.RollNo,
                student.FirstName, student.LastName, student.Class, student.Section,
                student.Dob, student.FatherName, student.MotherName,
            },
            academicYear = year,
            exams = examData.Select(e => new
            {
                exam = new
                {
                    _id = e.Exam.Id, name = e.Exam.Name,
                    type = EnumWireParse.ToWire(e.Exam.Type),
                    fromDate = e.Exam.FromDate, toDate = e.Exam.ToDate,
                    status = EnumWireParse.ToWire(e.Exam.Status),
                    publishedAt = e.Exam.PublishedAt,
                    weightInFinal = e.Exam.WeightInFinal,
                },
                gradingScale = e.Scale is null ? null
                    : new { name = e.Scale.Name, type = EnumWireParse.ToWire(e.Scale.Type) },
                subjects = e.Subjects.Select(s => new
                {
                    subjectId = s.SubjectId, subjectName = s.SubjectName,
                    maxMarks = s.MaxMarks, marksObtained = s.MarksObtained,
                    percentage = s.Percentage, grade = s.Grade, gpa = s.Gpa,
                    status = s.Status, isPassing = s.IsPassing,
                }),
                totals = new
                {
                    totalObtained = e.TotalObtained, totalMax = e.TotalMax,
                    overallPct = e.OverallPct, overallGrade = e.OverallGrade,
                },
            }),
            composite,
        };

        return new BuiltCard(payload, student, school, year, examData,
            compositeTyped, compositeWeights, compositeOverall);
    }

    private sealed record SchoolHeader(
        string Name, string? NameHindi, string? Address, string? City,
        string? State, string? Pincode, string? LogoUrl, string AcademicYear);

    private sealed record BuiltCard(
        object Payload, Student Student, SchoolHeader? School, string Year,
        List<ExamCard> Exams, List<CompositeSubject>? Composite,
        decimal CompositeWeights, decimal CompositeOverall);

    private sealed record CompositeSubject(Guid SubjectId, string SubjectName, decimal FinalPercentage);

    private sealed class SubjectRow
    {
        public Guid SubjectId { get; init; }
        public string SubjectName { get; init; } = "";
        public decimal MaxMarks { get; init; }
        public decimal? MarksObtained { get; init; }
        public decimal? Percentage { get; init; }
        public string? Grade { get; init; }
        public decimal? Gpa { get; init; }
        public string Status { get; init; } = "";
        public bool? IsPassing { get; init; }
    }

    private sealed class ExamCard
    {
        public Exam Exam { get; init; } = null!;
        public GradingScale? Scale { get; init; }
        public List<SubjectRow> Subjects { get; init; } = [];
        public decimal TotalObtained { get; init; }
        public decimal TotalMax { get; init; }
        public decimal OverallPct { get; init; }
        public string? OverallGrade { get; init; }
    }
}
