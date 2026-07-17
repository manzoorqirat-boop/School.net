using System.Text.Json.Serialization;

namespace QMSoft.Api.Domain.Entities;

/// <summary>
/// The tenant root. Deliberately NOT ITenantScoped — it *is* the tenant, and a
/// filter on it would be self-referential.
///
/// api.ts caches this in localStorage as 'vy_school' at login and reads it on
/// most pages (API.school()), so the login payload's `school` object must carry
/// whatever those pages touch.
/// </summary>
public class School : IEntity, IAuditable
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    // ── Identity ──────────────────────────────────────────────────────────
    public string Name { get; set; } = "";
    public string? NameHindi { get; set; }

    /// <summary>Globally unique, lowercased. citext — see SCHEMA-MAP §4.1.</summary>
    public string Slug { get; set; } = "";

    public SchoolType Type { get; set; } = SchoolType.K12;

    // ── Contact ───────────────────────────────────────────────────────────
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    // ── Branding ──────────────────────────────────────────────────────────
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#1e40af";

    // ── Academic config ───────────────────────────────────────────────────
    public string AcademicYear { get; set; } = "2025-2026";

    /// <summary>
    /// Calendar month the school year begins. India = 4 (April).
    /// Drives "current academic year" auto-detection across the app.
    /// </summary>
    public int AcademicYearStartMonth { get; set; } = 4;

    /// <summary>text[] — simple lists, never queried individually (SCHEMA-MAP §14.7).</summary>
    public List<string> Classes { get; set; } = [];

    public List<string> Sections { get; set; } = ["A", "B", "C"];

    /// <summary>'Mon'..'Sun'. Kept as text[] to match the Mongo string enum.</summary>
    public List<string> WorkingDays { get; set; } = [];

    // ── Leave policy ──────────────────────────────────────────────────────
    /// <summary>
    /// Child table, not JSONB: seeds Leave.types per teacher-year, and the
    /// controller reads it to decide whether to seed Indian-school defaults.
    /// Mongo `default: undefined` means "unset" is meaningful and distinct from
    /// "empty" — an empty list here means the controller seeds defaults.
    /// </summary>
    public List<SchoolLeaveType> LeaveTypes { get; set; } = [];

    /// <summary>Teachers' leaves need principal approval.</summary>
    public bool LeaveRequireApproval { get; set; } = true;

    // ── Fee automation (feeSchedule) ──────────────────────────────────────
    // Flattened from the nested Mongo subdoc — 4 scalars don't earn a table.
    // billingDay/reminderDay are 1..28, capped so they exist in February.
    public bool FeeBillingEnabled { get; set; }
    public int FeeBillingDay { get; set; } = 1;
    public bool FeeReminderEnabled { get; set; }
    public int FeeReminderDay { get; set; } = 10;

    // ── Subscription ──────────────────────────────────────────────────────
    public SchoolPlan Plan { get; set; } = SchoolPlan.Trial;
    public DateTime? PlanExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Razorpay (per-school) ─────────────────────────────────────────────
    public string? RazorpayKeyId { get; set; }

    /// <summary>
    /// ENCRYPTED at rest (AES-256-GCM via EF ValueConverter).
    ///
    /// [JsonIgnore] is a DELIBERATE deviation. The Mongo model sets
    /// toJSON: { getters: true }, so this decrypts into every school payload —
    /// including the `school` object returned by /api/auth/login, which api.ts
    /// writes to localStorage as 'vy_school'. That puts a live payment secret in
    /// the browser of every user of that school.
    ///
    /// Nothing in the frontend reads it. Server-side callers use the entity
    /// property directly, which is unaffected by JSON attributes.
    /// </summary>
    [JsonIgnore]
    public string? RazorpayKeySecret { get; set; }

    // ── UPI / QR (no API keys needed) ─────────────────────────────────────
    /// <summary>e.g. "school@hdfcbank" — feeds the upi://pay deep link.</summary>
    public string? PaymentVpa { get; set; }

    /// <summary>Name shown in the payer's UPI app.</summary>
    public string? PaymentPayeeName { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    [JsonIgnore] public ICollection<User> Users { get; set; } = [];
    [JsonIgnore] public ICollection<Student> Students { get; set; } = [];

    // ── Domain ────────────────────────────────────────────────────────────

    /// <summary>Port of planGuard.js expiry check.</summary>
    public bool IsPlanExpired => PlanExpiresAt.HasValue && PlanExpiresAt.Value < DateTime.UtcNow;

    /// <summary>
    /// Port of planAllows(school, feature). Expiry beats plan rank — an expired
    /// 'pro' school gets nothing.
    /// </summary>
    public bool PlanAllows(PlanFeature feature)
    {
        if (IsPlanExpired) return false;

        return feature switch
        {
            PlanFeature.Payroll         => Plan >= SchoolPlan.Pro,
            PlanFeature.ApiAccess       => Plan >= SchoolPlan.Pro,
            PlanFeature.AdvancedReports => Plan >= SchoolPlan.Basic,
            _ => false,
        };
    }

    /// <summary>null = unlimited. Matches PLAN_LIMITS.maxStudents.</summary>
    public int? MaxStudents => Plan switch
    {
        SchoolPlan.Trial => 50,
        SchoolPlan.Basic => 500,
        _ => null,
    };
}

public enum PlanFeature { Payroll, ApiAccess, AdvancedReports }

/// <summary>School-level leave policy — the template Leave.types is seeded from.</summary>
public class SchoolLeaveType : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid SchoolId { get; set; }
    [JsonIgnore] public School School { get; set; } = null!;

    public string Name { get; set; } = "";

    /// <summary>numeric(5,1) — half-days are real in Indian schools.</summary>
    public decimal TotalDays { get; set; }

    public bool IsPaid { get; set; } = true;
    public string? Color { get; set; }
    public string? Description { get; set; }
}
