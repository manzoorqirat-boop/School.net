using System.Text.Json.Serialization;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Common;

/// <summary>
/// Explicit request shapes for the endpoints that previously bound DOMAIN
/// ENTITIES straight from the request body.
///
/// Two problems with `[FromBody] Student`:
///
///  1. MASS ASSIGNMENT / TENANT CROSSING. The entity exposes setters for
///     schoolId, isDeleted, shareToken and _id. A caller could post any of
///     them. Create overwrote SchoolId afterwards and ApplyUpdate copied a
///     hand-picked subset, but nothing structurally prevented the rest — the
///     safety was a convention, one refactor away from being lost.
///
///  2. THE SAVE BUG. Every non-nullable entity property without an
///     initializer became a required body field, so server-owned columns the
///     client never sends (shareEnabled, isDeleted, createdAt, weightInFinal…)
///     produced a 400 before the action ran.
///
/// Every property here is nullable on purpose: absent means "not supplied",
/// which the controller distinguishes from an explicit value. Required-ness is
/// enforced in the action (or FluentValidation), where a proper error envelope
/// can be returned — never by the model binder.
/// </summary>
public sealed class StudentWriteRequest
{
    // ── Identity ──────────────────────────────────────────────────────────
    public string? AdmissionNo { get; set; }
    public string? RollNo { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FirstNameHi { get; set; }
    public string? LastNameHi { get; set; }
    public DateOnly? Dob { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<Gender>))]
    public Gender? Gender { get; set; }

    public string? BloodGroup { get; set; }

    // ── Placement ─────────────────────────────────────────────────────────
    public string? Class { get; set; }
    public string? Section { get; set; }
    public string? AcademicYear { get; set; }

    /// <summary>
    /// Nullable here even though the column is NOT NULL: the client may legally
    /// omit it, and Create defaults it to today rather than rejecting the save.
    /// </summary>
    public DateOnly? AdmissionDate { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<StudentStatus>))]
    public StudentStatus? Status { get; set; }

    // ── Contact ───────────────────────────────────────────────────────────
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    // ── Parents / guardian ────────────────────────────────────────────────
    public string? FatherName { get; set; }
    public string? FatherNameHi { get; set; }
    public string? FatherPhone { get; set; }
    public string? FatherOccup { get; set; }
    public string? MotherName { get; set; }
    public string? MotherNameHi { get; set; }
    public string? MotherPhone { get; set; }
    public string? MotherOccup { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianRel { get; set; }

    // ── Documents ─────────────────────────────────────────────────────────
    /// <summary>Masked by Student.AadharNo's setter on assignment — the DPDP
    /// control stays on the entity, so it cannot be bypassed via this DTO.</summary>
    public string? AadharNo { get; set; }
    public string? AadharDoc { get; set; }
    public string? AadharDocKey { get; set; }
    public string? BirthCertNo { get; set; }
    public string? BirthDoc { get; set; }
    public string? BirthDocKey { get; set; }

    // ── Demographics ──────────────────────────────────────────────────────
    [JsonConverter(typeof(EmptyStringToNullEnumConverter<StudentCategory>))]
    public StudentCategory? Category { get; set; }

    public string? Caste { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<Religion>))]
    public Religion? Religion { get; set; }

    public string? MotherTongue { get; set; }
    public string? Nationality { get; set; }

    // ── Transport ─────────────────────────────────────────────────────────
    [JsonConverter(typeof(EmptyStringToNullEnumConverter<TransportMode>))]
    public TransportMode? TransportMode { get; set; }

    public string? BusRoute { get; set; }
    public string? PickupPoint { get; set; }

    // ── Previous school ───────────────────────────────────────────────────
    public string? PrevSchool { get; set; }
    public string? PrevClass { get; set; }
    public string? TcNo { get; set; }
    public DateOnly? TcDate { get; set; }
    public string? TcDoc { get; set; }
    public string? TcDocKey { get; set; }

    // ── Misc ──────────────────────────────────────────────────────────────
    public string? House { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Notes { get; set; }

    // ── Child collections ─────────────────────────────────────────────────
    // Null = "not editing these"; empty list = "clear them". Preserves the
    // existing ApplyUpdate semantics exactly.
    public List<SiblingDto>? Siblings { get; set; }
    public List<PassedExamDto>? PassedExams { get; set; }

    // NOTE: no _id, schoolId, isDeleted, deletedAt, shareToken or shareEnabled.
    // Those are server-owned. Sharing is toggled via POST/DELETE /:id/share.

    public sealed class SiblingDto
    {
        public string? Name { get; set; }
        public string? Class { get; set; }

        [JsonConverter(typeof(EmptyStringToNullEnumConverter<SiblingRelation>))]
        public SiblingRelation? Relation { get; set; }

        /// <summary>Defaults to true when omitted, matching the entity.</summary>
        public bool? SameSchool { get; set; }
    }

    public sealed class PassedExamDto
    {
        public string? ExamName { get; set; }
        public string? Institution { get; set; }
        public string? Year { get; set; }
        public string? RollNo { get; set; }
        public string? Board { get; set; }
        public decimal? MaxMarks { get; set; }
        public decimal? ObtainedMarks { get; set; }
    }
}

/// <summary>POST /api/exams. `subjects` is create-only — Update ignores it,
/// matching the existing controller behaviour.</summary>
public sealed class ExamWriteRequest
{
    public string? Name { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<ExamType>))]
    public ExamType? Type { get; set; }

    public string? AcademicYear { get; set; }
    public string? Class { get; set; }
    public string? Section { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>Omitted by the mobile create form → defaults to 0.</summary>
    public decimal? WeightInFinal { get; set; }

    public Guid? GradingScaleId { get; set; }
    public string? Notes { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<ExamStatus>))]
    public ExamStatus? Status { get; set; }

    public List<ExamSubjectDto>? Subjects { get; set; }

    public sealed class ExamSubjectDto
    {
        public Guid? SubjectId { get; set; }
        public string? SubjectName { get; set; }
        public decimal? MaxMarks { get; set; }

        /// <summary>Overrides the scale's passing mark for this subject only.</summary>
        public decimal? PassingMark { get; set; }

        public decimal? TheoryMax { get; set; }
        public decimal? PracticalMax { get; set; }
        public DateOnly? ExamDate { get; set; }

        /// <summary>"09:00" — a string on the wire and in the entity.</summary>
        public string? StartTime { get; set; }

        public int? DurationMins { get; set; }
    }
}

/// <summary>POST/PUT /api/polls. createdBy and schoolId are server-owned.</summary>
public sealed class PollWriteRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<PollCategory>))]
    public PollCategory? Category { get; set; }

    public List<string>? TargetRoles { get; set; }
    public List<PollQuestionDto>? Questions { get; set; }

    [JsonConverter(typeof(EmptyStringToNullEnumConverter<PollStatus>))]
    public PollStatus? Status { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? ShowResultsBeforeClose { get; set; }
    public bool? AllowAnonymous { get; set; }

    public sealed class PollQuestionDto
    {
        public string? Text { get; set; }
        public List<PollOptionDto>? Options { get; set; }
    }

    public sealed class PollOptionDto
    {
        public string? Text { get; set; }
    }
}

/// <summary>POST/PUT /api/timetables. Entries are managed by
/// POST /:id/entries, not here.</summary>
public sealed class TimetableWriteRequest
{
    public string? Class { get; set; }
    public string? Section { get; set; }
    public string? AcademicYear { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string? Term { get; set; }
}

/// <summary>PUT /api/schools/:id — settings only. Slug, plan, isActive and the
/// timestamps are deliberately absent: changing a tenant's slug or plan is not
/// a settings-form operation.</summary>
public sealed class SchoolUpdateRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? AcademicYear { get; set; }
    public string? PrimaryColor { get; set; }
    public List<string>? Classes { get; set; }
    public List<string>? Sections { get; set; }
    public List<string>? WorkingDays { get; set; }
    public int? FeeBillingDay { get; set; }
    public int? FeeReminderDay { get; set; }
    public bool? LeaveRequireApproval { get; set; }
}

/// <summary>POST /api/class-teachers.</summary>
public sealed class ClassTeacherWriteRequest
{
    public Guid? TeacherUserId { get; set; }
    public string? AcademicYear { get; set; }
    public string? Class { get; set; }
    public string? Section { get; set; }

    /// <summary>Null = general/homeroom assignment. Part of the UNIQUE key.</summary>
    public string? Subject { get; set; }

    public bool? IsPrimary { get; set; }
    public bool? IsActive { get; set; }
}
