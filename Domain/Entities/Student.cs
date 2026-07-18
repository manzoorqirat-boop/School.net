using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace QMSoft.Api.Domain.Entities;

public class Student : TenantEntity, ISoftDeletable
{
    // ── Identity ──────────────────────────────────────────────────────────
    public string AdmissionNo { get; set; } = "";
    public string? RollNo { get; set; }

    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }

    /// <summary>Hindi transliterations — fed by GET /api/lookups/transliterate.</summary>
    public string? FirstNameHi { get; set; }
    public string? LastNameHi { get; set; }

    public DateOnly? Dob { get; set; }

    /// <summary>'' on the wire ⇄ NULL in DB (API-CONTRACT §3.1).</summary>
    [JsonConverter(typeof(Common.EmptyStringToNullEnumConverter<Gender>))]
    public Gender? Gender { get; set; }

    public string? BloodGroup { get; set; }

    // ── Placement ─────────────────────────────────────────────────────────
    public string Class { get; set; } = "";
    public string Section { get; set; } = "";
    public string AcademicYear { get; set; } = "";
    public DateOnly AdmissionDate { get; set; }

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

    private string? _aadharNo;

    /// <summary>
    /// DPDP Act 2023: only the last 4 digits are stored, as "XXXXXXXX1234".
    ///
    /// The setter is the compliance control — it masks on WRITE, so a full
    /// number never reaches the database. Porting this as a plain property
    /// would silently start persisting full Aadhaar numbers: a regulatory
    /// breach that no test would catch, because reads look identical.
    ///
    /// Non-12-digit input passes through unchanged (partial / already-masked),
    /// matching the Mongo setter exactly.
    /// </summary>
    public string? AadharNo
    {
        get => _aadharNo;
        set => _aadharNo = Mask(value);
    }

    internal static string? Mask(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v;

        var digits = new string(v.Where(char.IsDigit).ToArray());
        return digits.Length == 12 ? $"XXXXXXXX{digits[^4..]}" : v;
    }

    public string? AadharDoc { get; set; }
    public string? AadharDocKey { get; set; }
    public string? BirthCertNo { get; set; }
    public string? BirthDoc { get; set; }
    public string? BirthDocKey { get; set; }

    // ── Demographics ──────────────────────────────────────────────────────
    [JsonConverter(typeof(Common.EmptyStringToNullEnumConverter<StudentCategory>))]
    public StudentCategory? Category { get; set; }

    public string? Caste { get; set; }

    [JsonConverter(typeof(Common.EmptyStringToNullEnumConverter<Religion>))]
    public Religion? Religion { get; set; }

    public string? MotherTongue { get; set; }
    public string Nationality { get; set; } = "Indian";

    // ── Transport ─────────────────────────────────────────────────────────
    [JsonConverter(typeof(Common.EmptyStringToNullEnumConverter<TransportMode>))]
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

    // ── Public share link ─────────────────────────────────────────────────

    /// <summary>
    /// Grants UNAUTHENTICATED read via GET /api/students/public/:token.
    ///
    /// [JsonIgnore]: the token is a bearer credential. Returning it in every
    /// student payload would hand it to any teacher/accountant who can list
    /// students, letting them mint public links for children they can see.
    /// POST /:id/share returns it explicitly, once.
    /// </summary>
    [JsonIgnore]
    public string? ShareToken { get; set; }

    public bool ShareEnabled { get; set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public StudentStatus Status { get; set; } = StudentStatus.Active;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // ── Children ──────────────────────────────────────────────────────────
    // Both are read AND written by StudentForm.tsx (lines 110-111, 291).
    public ICollection<StudentPassedExam> PassedExams { get; set; } = [];
    public ICollection<StudentSibling> Siblings { get; set; } = [];

    [JsonIgnore] public School School { get; set; } = null!;

    // ── Domain ────────────────────────────────────────────────────────────

    /// <summary>
    /// NOT serialized. The Mongo model has a fullName virtual but never enables
    /// virtuals in toJSON — so it never reaches the client, and every page
    /// concatenates firstName + lastName itself (attendance/page.tsx:578,
    /// exams/page.tsx:724). Adding it to the payload would be harmless but
    /// pointless; server-side callers use this.
    /// </summary>
    [JsonIgnore]
    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Port of studentSchema.methods.enableShare — 16 random bytes, hex.</summary>
    public string EnableShare()
    {
        ShareToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        ShareEnabled = true;
        return ShareToken;
    }

    /// <summary>
    /// Port of disableShare(). NOTE: the Mongo version leaves shareToken intact,
    /// so re-enabling reuses the old token. Preserved deliberately — DELETE
    /// /:id/share then POST /:id/share should behave identically to today.
    /// Clearing the token here would be safer but is a behaviour change.
    /// </summary>
    public void DisableShare() => ShareEnabled = false;
}

/// <summary>Prior-institution exam records. Child table (Mongo had _id implicitly).</summary>
public class StudentPassedExam : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid StudentId { get; set; }
    [JsonIgnore] public Student Student { get; set; } = null!;

    public string? ExamName { get; set; }
    public string? Institution { get; set; }

    /// <summary>Free text ('2019', '2019-20') — not an int.</summary>
    public string? Year { get; set; }

    public string? RollNo { get; set; }
    public string? Board { get; set; }

    /// <summary>
    /// Marks, not money — but still NUMERIC. A double would make 33.33% × 3
    /// subjects fail to sum to 100.
    /// </summary>
    public decimal? MaxMarks { get; set; }
    public decimal? ObtainedMarks { get; set; }
}

public class StudentSibling : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid StudentId { get; set; }
    [JsonIgnore] public Student Student { get; set; } = null!;

    public string? Name { get; set; }
    public string? Class { get; set; }

    /// <summary>'' ⇄ NULL — same frontend-default leak as Student.gender.</summary>
    [JsonConverter(typeof(Common.EmptyStringToNullEnumConverter<SiblingRelation>))]
    public SiblingRelation? Relation { get; set; }

    public bool SameSchool { get; set; } = true;
}
