using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<ExamType>))]
public enum ExamType
{
    [EnumMember(Value = "unit_test")]   UnitTest,
    [EnumMember(Value = "periodic")]    Periodic,
    [EnumMember(Value = "term")]        Term,
    [EnumMember(Value = "half_yearly")] HalfYearly,
    [EnumMember(Value = "annual")]      Annual,
    [EnumMember(Value = "custom")]      Custom,
}

[JsonConverter(typeof(EnumMemberJsonConverter<ExamStatus>))]
public enum ExamStatus
{
    [EnumMember(Value = "draft")]       Draft,
    [EnumMember(Value = "scheduled")]   Scheduled,
    [EnumMember(Value = "in_progress")] InProgress,
    [EnumMember(Value = "completed")]   Completed,
    [EnumMember(Value = "published")]   Published,
}

[JsonConverter(typeof(EnumMemberJsonConverter<ExamResultStatus>))]
public enum ExamResultStatus
{
    [EnumMember(Value = "absent")]  Absent,
    [EnumMember(Value = "present")] Present,
    [EnumMember(Value = "exempt")]  Exempt,
}

[JsonConverter(typeof(EnumMemberJsonConverter<GradingScaleType>))]
public enum GradingScaleType
{
    [EnumMember(Value = "marks")]     Marks,     // no auto-grade, raw score only
    [EnumMember(Value = "grade")]     Grade,     // letter grade from %
    [EnumMember(Value = "gpa")]       Gpa,       // grade + GPA point
    [EnumMember(Value = "pass_fail")] PassFail,
}

public class Exam : TenantEntity
{
    public string Name { get; set; } = "";
    public ExamType Type { get; set; }

    public string AcademicYear { get; set; } = "";
    public string Class { get; set; } = "";

    /// <summary>Null = applies to all sections. In a plain (non-unique) index only.</summary>
    public string? Section { get; set; }

    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    /// <summary>{_id: true} in Mongo → child table WITH serialized _id.</summary>
    public ICollection<ExamSubject> Subjects { get; set; } = [];

    public Guid? GradingScaleId { get; set; }
    [JsonIgnore] public GradingScale? GradingScale { get; set; }

    /// <summary>% this exam contributes to the final report-card rollup. 0–100.</summary>
    public decimal WeightInFinal { get; set; }

    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One paper inside an exam: max marks, theory/practical split, schedule.</summary>
public class ExamSubject : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid ExamId { get; set; }
    [JsonIgnore] public Exam Exam { get; set; } = null!;

    public Guid SubjectId { get; set; }
    [JsonIgnore] public Subject Subject { get; set; } = null!;

    /// <summary>Snapshot for reports — keep (SCHEMA-MAP §11).</summary>
    public string SubjectName { get; set; } = "";

    public decimal MaxMarks { get; set; }

    /// <summary>Overrides the scale's passing mark for this subject only.</summary>
    public decimal? PassingMark { get; set; }

    public decimal? TheoryMax { get; set; }
    public decimal? PracticalMax { get; set; }

    public DateOnly? ExamDate { get; set; }

    /// <summary>"09:00" — a string in Mongo AND in the frontend's time input. Kept.</summary>
    public string? StartTime { get; set; }

    public int? DurationMins { get; set; }
}

public class ExamResult : TenantEntity
{
    public Guid ExamId { get; set; }
    [JsonIgnore] public Exam Exam { get; set; } = null!;

    public Guid StudentId { get; set; }
    [JsonIgnore] public Student Student { get; set; } = null!;

    public Guid SubjectId { get; set; }
    [JsonIgnore] public Subject Subject { get; set; } = null!;

    // Snapshots — historical accuracy + report speed (SCHEMA-MAP §11).
    public string? ExamName { get; set; }
    public string? SubjectName { get; set; }
    public string? StudentName { get; set; }
    public string? StudentAdmNo { get; set; }
    public string? Class { get; set; }
    public string? Section { get; set; }
    public string? AcademicYear { get; set; }

    /// <summary>
    /// NULL MEANS ABSENT — not zero. A student who scored 0 has marksObtained=0,
    /// status=present, and can still be isPassing=false; an absent student has
    /// marksObtained=NULL. Collapsing the two corrupts every class average
    /// (AVG ignores NULLs — correctly — but would include spurious zeros).
    /// decimal?, never decimal.
    /// </summary>
    public decimal? MarksObtained { get; set; }

    public decimal MaxMarks { get; set; }
    public decimal? TheoryMarks { get; set; }
    public decimal? PracticalMarks { get; set; }

    // Auto-computed by Recompute() — never set directly by handlers.
    public decimal? Percentage { get; set; }
    public string? Grade { get; set; }
    public decimal? Gpa { get; set; }
    public bool? IsPassing { get; set; }

    public ExamResultStatus Status { get; set; } = ExamResultStatus.Present;
    public string? Remarks { get; set; }

    public Guid? EnteredByUserId { get; set; }
    [JsonIgnore] public User? EnteredByUser { get; set; }
    public string? EnteredByName { get; set; }

    /// <summary>Once published to parents, edits require admin override.</summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Port of the pre('save') hook — which in Node DID NOT FIRE on
    /// updateOne/findOneAndUpdate, meaning bulk marksheet paths could leave
    /// percentage stale. Here it is ONE method every write path calls; the
    /// marksheet upsert (Phase 3) must call it per row inside its transaction.
    ///
    /// Node math: percentage = Math.round(x*1000)/10 → 1 decimal, half UP.
    /// Grade/GPA come from the scale — the hook did not compute them; the
    /// controller called gradeFor(). Same split here: pass the scale in, or
    /// null to skip grading (scale type 'marks' handled inside GradeFor).
    /// </summary>
    public void Recompute(GradingScale? scale)
    {
        if (Status == ExamResultStatus.Absent)
        {
            MarksObtained = null;
            Percentage = 0m;
            IsPassing = false;
            Grade = null;
            Gpa = null;
            return;
        }

        if (MarksObtained is not null && MaxMarks > 0)
        {
            Percentage = Math.Round(
                MarksObtained.Value * 100m / MaxMarks, 1,
                MidpointRounding.AwayFromZero);
        }

        if (scale is not null && Percentage is not null)
        {
            var g = scale.GradeFor(Percentage.Value);
            Grade = g.Grade;
            Gpa = g.Gpa;
            IsPassing = g.IsPassing;
        }
    }
}

public class GradingScale : TenantEntity
{
    public string Name { get; set; } = "";
    public GradingScaleType Type { get; set; }

    /// <summary>Ordered by MinPercent descending for display; lookup scans all.</summary>
    public ICollection<GradeBand> Bands { get; set; } = [];

    /// <summary>% needed to pass — used by 'marks' and 'pass_fail' types.</summary>
    public decimal PassingMark { get; set; } = 33m;

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Grading logic — Node's gradeFor with the GAP BUG FIXED (product-owner
    /// approved; no production data means no historical report card to
    /// contradict).
    ///
    /// Node matched percent &gt;= min &amp;&amp; &lt;= max with INTEGER bands, so one-decimal
    /// percentages fell into the cracks: 109/120 = 90.8% → no band → grade '—',
    /// isPassing FALSE — a passing student marked failing.
    ///
    /// Fix: HALF-OPEN matching over bands sorted by MinPercent — a percent
    /// belongs to the highest band whose MinPercent it reaches. MaxPercent
    /// becomes display-only.
    ///
    /// Safety property (verified by simulation): every percentage the OLD rule
    /// graded gets the SAME grade under the new rule — only the '—' gap results
    /// change, and only to the grade of the band directly below the gap
    /// (90.8 → A2, 32.9 → E). Below the lowest MinPercent still yields '—'
    /// (a school defining no fail band keeps that behaviour).
    /// </summary>
    public GradeResult GradeFor(decimal percent)
    {
        if (Type is GradingScaleType.Marks or GradingScaleType.PassFail)
        {
            var pass = percent >= PassingMark;
            return new GradeResult(pass ? "PASS" : "FAIL", null, pass, null);
        }

        var band = Bands
            .Where(b => percent >= b.MinPercent)
            .OrderByDescending(b => b.MinPercent)
            .FirstOrDefault();

        if (band is null) return new GradeResult("—", null, false, null);

        return new GradeResult(
            band.Grade,
            Type == GradingScaleType.Gpa ? band.Gpa : null,
            band.IsPassing,
            band.Description);
    }
}

public sealed record GradeResult(string Grade, decimal? Gpa, bool IsPassing, string? Description);

/// <summary>
/// One band: 91–100 = A1 = 10 GPA, "Outstanding".
///
/// {_id: false} in Mongo — bands ship to the frontend as plain objects with NO
/// _id. The child table needs a PK, but it must NOT serialize: exam-config's
/// scale editor round-trips bands verbatim, and an unexpected _id would be
/// posted back and fail validation. Hence [JsonIgnore] on Id — the one child
/// table where the PK is internal-only.
/// </summary>
public class GradeBand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid GradingScaleId { get; set; }
    [JsonIgnore] public GradingScale GradingScale { get; set; } = null!;

    public decimal MinPercent { get; set; }
    public decimal MaxPercent { get; set; }

    public string Grade { get; set; } = "";

    /// <summary>CBSE-style 10-point. Null unless the scale type is 'gpa'.</summary>
    public decimal? Gpa { get; set; }

    public string? Description { get; set; }

    public bool IsPassing { get; set; } = true;
}
