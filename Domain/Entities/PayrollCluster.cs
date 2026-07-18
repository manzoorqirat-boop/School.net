using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<PayrollStatus>))]
public enum PayrollStatus
{
    [EnumMember(Value = "draft")]     Draft,
    [EnumMember(Value = "generated")] Generated,
    [EnumMember(Value = "locked")]    Locked,
    [EnumMember(Value = "paid")]      Paid,
    [EnumMember(Value = "failed")]    Failed,     // terminal — transfer errors
}

/// <summary>Overlapping names with PayrollStatus, DIFFERENT machine — two enums
/// on purpose (SCHEMA-MAP §5.5).</summary>
[JsonConverter(typeof(EnumMemberJsonConverter<PayrollRunStatus>))]
public enum PayrollRunStatus
{
    [EnumMember(Value = "draft")]              Draft,
    [EnumMember(Value = "generated")]          Generated,
    [EnumMember(Value = "locked")]             Locked,
    [EnumMember(Value = "transfer_queued")]    TransferQueued,
    [EnumMember(Value = "transfer_completed")] TransferCompleted,
    [EnumMember(Value = "cancelled")]          Cancelled,
}

[JsonConverter(typeof(EnumMemberJsonConverter<LeaveStatus>))]
public enum LeaveStatus
{
    [EnumMember(Value = "pending")]  Pending,
    [EnumMember(Value = "approved")] Approved,
    [EnumMember(Value = "rejected")] Rejected,
}

/// <summary>
/// ONE rounding rule for all payroll math — Node's
/// `round2 = Math.round(n*100)/100`, i.e. half UP. .NET's default is BANKER'S
/// (2.5→2), which would silently shift payslip totals by a rupee.
/// </summary>
public static class PayrollMath
{
    public static decimal Round2(decimal n) =>
        Math.Round(n, 2, MidpointRounding.AwayFromZero);
}

// ═════════════════════════════════════════════════════════════════════════════
// SalaryStructure — the blueprint. PERCENT fields and RUPEE fields share names
// with Payroll but mean different things (SCHEMA-MAP §5.1):
//
//   field       here (structure)         Payroll (payslip)
//   da, hra     % of BASE  (0–200)       ₹
//   ta          ₹ fixed                  ₹
//   pf, esi     % of BASE  (0–100)       ₹
//   profTax     ₹ fixed                  ₹
//   incomeTax   % of GROSS (0–100)       ₹     ← gross, not base!
//
// otherAllowances items with isPercentage → % of BASE.
// otherDeductions items with isPercentage → % of GROSS.
// ═════════════════════════════════════════════════════════════════════════════

public class SalaryStructure : TenantEntity
{
    public Guid TeacherId { get; set; }
    [JsonIgnore] public User Teacher { get; set; } = null!;

    public string AcademicYear { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public decimal BaseSalary { get; set; }                 // ₹

    // Allowances
    public decimal Da { get; set; }                         // % of base, 0–200
    public decimal Hra { get; set; }                        // % of base, 0–200
    public decimal Ta { get; set; }                         // ₹ fixed
    public List<SalaryComponentItem> OtherAllowances { get; set; } = [];   // JSONB

    // Deductions
    public decimal Pf { get; set; }                         // % of base, 0–100
    public decimal Esi { get; set; }                        // % of base, 0–100
    public decimal ProfessionalTax { get; set; }            // ₹ fixed
    public decimal IncomeTax { get; set; }                  // % of GROSS, 0–100
    public List<SalaryComponentItem> OtherDeductions { get; set; } = [];   // JSONB

    /// <summary>₹/day; 0 or unset ⇒ baseSalary/30 (industry default).</summary>
    public decimal LeaveDeductionPerDay { get; set; }

    // ── Bank (ENCRYPTED at rest; IFSC uppercased BEFORE encryption) ───────
    public string? BankAccountNumber { get; set; }

    private string? _bankIfsc;
    /// <summary>Uppercase-then-encrypt: equality on ciphertext only works if
    /// the canonical (upper) form is what got encrypted — the Node model's
    /// whole reason for a custom setter. Normalised here, encrypted by the
    /// ValueConverter.</summary>
    public string? BankIfsc
    {
        get => _bankIfsc;
        set => _bankIfsc = value != null && !value.StartsWith("enc:v1:")
            ? value.Trim().ToUpperInvariant()
            : value;
    }

    public string? BankAccountHolder { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Computation (verbatim ports; PayrollMath.Round2 everywhere) ───────

    public GrossBreakdown ComputeGrossBreakdown()
    {
        var b = BaseSalary;
        var da = PayrollMath.Round2(b * Da / 100m);
        var hra = PayrollMath.Round2(b * Hra / 100m);
        var ta = PayrollMath.Round2(Ta);

        decimal otherTotal = 0m;
        var items = OtherAllowances.Select(a =>
        {
            var amt = a.IsPercentage
                ? PayrollMath.Round2(b * a.Amount / 100m)      // % of BASE
                : PayrollMath.Round2(a.Amount);
            otherTotal += amt;
            return new PayslipLineItem { Name = a.Name, Amount = amt };
        }).ToList();
        otherTotal = PayrollMath.Round2(otherTotal);

        var gross = PayrollMath.Round2(b + da + hra + ta + otherTotal);
        return new GrossBreakdown(b, da, hra, ta, items, otherTotal, gross);
    }

    public DeductionBreakdown ComputeDeductionBreakdown(decimal? grossSalary = null)
    {
        var b = BaseSalary;
        var gross = grossSalary ?? ComputeGrossBreakdown().GrossSalary;

        var pf = PayrollMath.Round2(b * Pf / 100m);
        var esi = PayrollMath.Round2(b * Esi / 100m);
        var pt = PayrollMath.Round2(ProfessionalTax);
        var it = PayrollMath.Round2(gross * IncomeTax / 100m);   // GROSS base!

        decimal otherTotal = 0m;
        var items = OtherDeductions.Select(d =>
        {
            var amt = d.IsPercentage
                ? PayrollMath.Round2(gross * d.Amount / 100m)   // % of GROSS
                : PayrollMath.Round2(d.Amount);
            otherTotal += amt;
            return new PayslipLineItem { Name = d.Name, Amount = amt };
        }).ToList();
        otherTotal = PayrollMath.Round2(otherTotal);

        var fixedTotal = PayrollMath.Round2(pf + esi + pt + it + otherTotal);
        return new DeductionBreakdown(pf, esi, pt, it, items, otherTotal, fixedTotal);
    }

    public decimal DailyRate() =>
        LeaveDeductionPerDay > 0
            ? PayrollMath.Round2(LeaveDeductionPerDay)
            : PayrollMath.Round2(BaseSalary / 30m);
}

/// <summary>{_id:false} JSONB element on the STRUCTURE — amount may be a %.</summary>
public sealed class SalaryComponentItem
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsPercentage { get; set; }
}

/// <summary>{_id:false} JSONB element on the PAYSLIP — always concrete ₹.</summary>
public sealed class PayslipLineItem
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed record GrossBreakdown(
    decimal Base, decimal Da, decimal Hra, decimal Ta,
    List<PayslipLineItem> OtherAllowanceItems, decimal OtherAllowanceTotal,
    decimal GrossSalary);

public sealed record DeductionBreakdown(
    decimal Pf, decimal Esi, decimal ProfessionalTax, decimal IncomeTax,
    List<PayslipLineItem> OtherDeductionItems, decimal OtherDeductionTotal,
    decimal FixedDeductions);

// ═════════════════════════════════════════════════════════════════════════════

/// <summary>One teacher's payslip for one month. EVERY component is concrete
/// rounded ₹ — the structure can change later; historic payslips must not.</summary>
public class Payroll : TenantEntity
{
    public Guid TeacherId { get; set; }
    [JsonIgnore] public User Teacher { get; set; } = null!;

    public Guid? PayrollRunId { get; set; }
    [JsonIgnore] public PayrollRun? PayrollRun { get; set; }

    public Guid? SalaryStructureId { get; set; }
    [JsonIgnore] public SalaryStructure? SalaryStructure { get; set; }

    public int Month { get; set; }                          // 1–12
    public int Year { get; set; }
    public string? AcademicYear { get; set; }

    // Teacher snapshot — payslip is self-contained.
    public string? TeacherName { get; set; }
    public string? TeacherEmail { get; set; }
    public string? TeacherUsername { get; set; }

    // Bank snapshot — ENCRYPTED, independent of the structure's copy.
    public string? BankAccount { get; set; }

    private string? _bankIfsc;
    public string? BankIfsc
    {
        get => _bankIfsc;
        set => _bankIfsc = value != null && !value.StartsWith("enc:v1:")
            ? value.Trim().ToUpperInvariant()
            : value;
    }

    public string? BankAccountHolder { get; set; }

    // Earnings — all ₹.
    public decimal BaseSalary { get; set; }
    public decimal Da { get; set; }
    public decimal Hra { get; set; }
    public decimal Ta { get; set; }
    public decimal OtherAllowances { get; set; }
    public List<PayslipLineItem> OtherAllowanceItems { get; set; } = [];   // JSONB
    public decimal GrossSalary { get; set; }

    // Deductions — all ₹.
    public decimal Pf { get; set; }
    public decimal Esi { get; set; }
    public decimal ProfessionalTax { get; set; }
    public decimal IncomeTax { get; set; }
    public decimal LeaveDeduction { get; set; }
    public decimal OtherDeductions { get; set; }
    public List<PayslipLineItem> OtherDeductionItems { get; set; } = [];   // JSONB
    public decimal TotalDeductions { get; set; }

    public decimal NetSalary { get; set; }

    public decimal UnpaidLeaveDays { get; set; }            // numeric(5,1) — half-days
    public decimal DailyRate { get; set; }

    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;

    // Razorpay transfer
    public string? RazorpayTransferId { get; set; }
    public DateTime? TransferredAt { get; set; }
    public string? TransferStatus { get; set; }
    public string? TransferFailureReason { get; set; }

    public string? Notes { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public Guid? LockedByUserId { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? PaidByUserId { get; set; }

    /// <summary>toJSON virtuals:true put this ON THE WIRE — 'October 2025'.</summary>
    [JsonPropertyName("periodLabel")]
    public string PeriodLabel =>
        $"{(Month is >= 1 and <= 12 ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month) : "")} {Year}".Trim();

    // ── Domain ────────────────────────────────────────────────────────────

    /// <summary>Idempotent — the pre('save') recompute. net = max(0, gross − ded).</summary>
    public void RecomputeNet() =>
        NetSalary = Math.Max(0m, PayrollMath.Round2(GrossSalary - TotalDeductions));

    /// <summary>
    /// The convention half of safeUpsertForGeneration: throws on locked/paid
    /// unless forced. The DB trigger (PayrollTriggerSql) is the structural
    /// half — this gives the clean 409, the trigger guarantees no bypass.
    /// </summary>
    public void GuardRegeneration(bool force = false)
    {
        if (!force && Status is PayrollStatus.Locked or PayrollStatus.Paid)
            throw new AppException($"Payslip is already {Status.ToString().ToLowerInvariant()}",
                409, ErrorCodes.DuplicateError);
    }

    /// <summary>
    /// Node quirk preserved: regeneration (Object.assign → status='generated')
    /// left the lockedAt/paidAt clears as dead code, but the NET effect on a
    /// force-regenerated locked slip was status reset + stale audit stamps.
    /// Here regeneration explicitly clears them — same observable payslip,
    /// honest audit trail.
    /// </summary>
    public void ApplyRegeneration()
    {
        Status = PayrollStatus.Generated;
        GeneratedAt = DateTime.UtcNow;
        LockedAt = null; LockedByUserId = null;
        PaidAt = null; PaidByUserId = null;
    }

    /// <summary>
    /// Static factory — port of buildPayslipData. Reads the structure's
    /// DECRYPTED bank fields (ValueConverter decrypts on materialisation) and
    /// snapshots them; the payslip's own converter re-encrypts with a fresh IV.
    /// unpaidLeaveDays comes from Leave.UnpaidLeaveDaysInMonth or
    /// TeacherAttendanceRules.UnpaidDays — never recomputed here.
    /// </summary>
    public static Payroll BuildPayslip(
        SalaryStructure s, User teacher, int month, int year,
        string? academicYear = null, decimal unpaidLeaveDays = 0m,
        Guid? payrollRunId = null)
    {
        var earnings = s.ComputeGrossBreakdown();
        var deductions = s.ComputeDeductionBreakdown(earnings.GrossSalary);

        var dailyRate = s.DailyRate();
        var unpaid = Math.Max(0m, unpaidLeaveDays);
        var leaveDed = PayrollMath.Round2(unpaid * dailyRate);

        var totalDed = PayrollMath.Round2(deductions.FixedDeductions + leaveDed);
        var net = Math.Max(0m, PayrollMath.Round2(earnings.GrossSalary - totalDed));

        return new Payroll
        {
            SchoolId = s.SchoolId,
            TeacherId = teacher.Id,
            SalaryStructureId = s.Id,
            PayrollRunId = payrollRunId,
            Month = month,
            Year = year,
            AcademicYear = academicYear ?? s.AcademicYear,

            TeacherName = teacher.Name,
            TeacherEmail = teacher.Email,
            TeacherUsername = teacher.Username,
            BankAccount = s.BankAccountNumber,
            BankIfsc = s.BankIfsc,
            BankAccountHolder = s.BankAccountHolder,

            BaseSalary = earnings.Base,
            Da = earnings.Da,
            Hra = earnings.Hra,
            Ta = earnings.Ta,
            OtherAllowances = earnings.OtherAllowanceTotal,
            OtherAllowanceItems = earnings.OtherAllowanceItems,
            GrossSalary = earnings.GrossSalary,

            Pf = deductions.Pf,
            Esi = deductions.Esi,
            ProfessionalTax = deductions.ProfessionalTax,
            IncomeTax = deductions.IncomeTax,
            LeaveDeduction = leaveDed,
            OtherDeductions = deductions.OtherDeductionTotal,
            OtherDeductionItems = deductions.OtherDeductionItems,
            TotalDeductions = totalDed,
            NetSalary = net,

            UnpaidLeaveDays = unpaid,
            DailyRate = dailyRate,

            Status = PayrollStatus.Generated,
            GeneratedAt = DateTime.UtcNow,
        };
    }
}

// ═════════════════════════════════════════════════════════════════════════════

public class PayrollRun : TenantEntity
{
    public string Name { get; set; } = "";                  // 'October 2025 Payroll'
    public int Month { get; set; }
    public int Year { get; set; }
    public string? AcademicYear { get; set; }

    // Counts — denormalised aggregates; recomputed by the payroll service
    // inside the same transaction as payslip writes (API-CONTRACT §3.3 rule).
    public int TotalTeachers { get; set; }
    public int PayrollsGenerated { get; set; }
    public int PayrollsSkipped { get; set; }
    public int PayrollsLocked { get; set; }
    public int PayrollsPaid { get; set; }
    public int PayrollsFailed { get; set; }

    // Money totals — ₹.
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNet { get; set; }

    /// <summary>Diagnostic log — {_id:false}, never queried → JSONB.</summary>
    public List<SkippedTeacher> Skipped { get; set; } = [];

    public string? RazorpayBatchId { get; set; }
    public string? BatchTransferId { get; set; }
    public string? BatchStatus { get; set; }                // free text in Node — kept

    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

    public Guid? InitiatedByUserId { get; set; }
    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? GeneratedAt { get; set; }
    public DateTime? LockedAt { get; set; }
    public DateTime? TransferredAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class SkippedTeacher
{
    public Guid? TeacherId { get; set; }
    public string? Name { get; set; }
    public string? Reason { get; set; }
}

// ═════════════════════════════════════════════════════════════════════════════

/// <summary>One doc per teacher-year: entitlements (types) + usage (records).</summary>
public class Leave : TenantEntity
{
    public Guid TeacherId { get; set; }
    [JsonIgnore] public User Teacher { get; set; } = null!;

    public string AcademicYear { get; set; } = "";

    public ICollection<LeaveType> Types { get; set; } = [];
    public ICollection<LeaveRecord> Records { get; set; } = [];

    /// <summary>
    /// Port of unpaidLeaveDaysInMonth(year, month) — feeds the salary
    /// deduction. Overlap window is INCLUSIVE both ends; day count is
    /// (end − start) + 1. Only APPROVED records of an UNPAID type count.
    ///
    /// ⚠️ Matching is BY NAME (records.type → types.name) — the latent-bug
    /// join. LeaveRecord now carries a nullable LeaveTypeId FK; when set it
    /// wins, name is the legacy fallback. Renaming a type without the FK
    /// silently stops its unpaid deductions (salaries go UP) — exactly the
    /// Node failure mode, now escapable.
    /// </summary>
    public decimal UnpaidLeaveDaysInMonth(int year, int month)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        decimal days = 0m;
        foreach (var rec in Records)
        {
            if (rec.Status != LeaveStatus.Approved) continue;

            var type = rec.LeaveTypeId is not null
                ? Types.FirstOrDefault(t => t.Id == rec.LeaveTypeId)
                : Types.FirstOrDefault(t => t.Name == rec.Type);
            if (type is null || type.IsPaid) continue;

            if (rec.ToDate < monthStart || rec.FromDate > monthEnd) continue;

            var start = rec.FromDate > monthStart ? rec.FromDate : monthStart;
            var end = rec.ToDate < monthEnd ? rec.ToDate : monthEnd;
            days += end.DayNumber - start.DayNumber + 1;
        }
        return days;
    }
}

/// <summary>{_id:false} in Mongo but a CHILD TABLE (SCHEMA-MAP §5.3): records
/// FK it, and balance is a generated column. PK internal, not serialized.</summary>
public class LeaveType
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid LeaveId { get; set; }
    [JsonIgnore] public Leave Leave { get; set; } = null!;

    public string Name { get; set; } = "";                  // 'Sick', 'Casual', …
    public decimal TotalDays { get; set; }                  // numeric(5,1)
    public decimal UsedDays { get; set; }

    /// <summary>STORED GENERATED (total − used) — replaces the pre('save') loop.</summary>
    public decimal BalanceDays { get; private set; }

    /// <summary>false ⇒ feeds the salary deduction.</summary>
    public bool IsPaid { get; set; } = true;
}

/// <summary>{_id:true} → child table, _id serialized:
/// POST /leaves/:leaveDocId/records/:recordId/approve addresses by it.</summary>
public class LeaveRecord : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid LeaveId { get; set; }
    [JsonIgnore] public Leave Leave { get; set; } = null!;

    /// <summary>Display snapshot + legacy name-join fallback.</summary>
    public string Type { get; set; } = "";

    /// <summary>The latent-bug fix: survives type renames. Nullable so the
    /// wire shape is unchanged for clients that never send it.</summary>
    public Guid? LeaveTypeId { get; set; }

    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    public decimal Days { get; set; }                       // numeric(5,1) — 0.5 real
    public string? Reason { get; set; }

    public LeaveStatus Status { get; set; } = LeaveStatus.Approved;
    public Guid? ApprovedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
