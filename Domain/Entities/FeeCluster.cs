using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<FeeFrequency>))]
public enum FeeFrequency
{
    [EnumMember(Value = "one_time")]    OneTime,
    [EnumMember(Value = "monthly")]     Monthly,
    [EnumMember(Value = "quarterly")]   Quarterly,
    [EnumMember(Value = "half_yearly")] HalfYearly,
    [EnumMember(Value = "annual")]      Annual,
}

/// <summary>
/// FIVE members on the wire, FOUR ever stored.
///
/// 'overdue' is pure derivation (dueDate vs now) — a stored value would be
/// wrong between scheduler runs (resolved decision, API-CONTRACT §6.2). The DB
/// enum carries the label for wire compatibility, but a CHECK forbids storing
/// it, and FeeInvoice serializes EffectiveStatus instead of the raw column.
/// </summary>
[JsonConverter(typeof(EnumMemberJsonConverter<InvoiceStatus>))]
public enum InvoiceStatus
{
    [EnumMember(Value = "pending")]   Pending,
    [EnumMember(Value = "partial")]   Partial,
    [EnumMember(Value = "paid")]      Paid,
    [EnumMember(Value = "overdue")]   Overdue,     // wire-only — never stored
    [EnumMember(Value = "cancelled")] Cancelled,
}

[JsonConverter(typeof(EnumMemberJsonConverter<PaymentMethod>))]
public enum PaymentMethod
{
    [EnumMember(Value = "cash")]          Cash,
    [EnumMember(Value = "cheque")]        Cheque,
    [EnumMember(Value = "upi")]           Upi,
    [EnumMember(Value = "card")]          Card,
    [EnumMember(Value = "bank_transfer")] BankTransfer,
    [EnumMember(Value = "razorpay")]      Razorpay,   // server had it, contracts.ts didn't — server wins (§3.2)
}

/// <summary>
/// Server enum wins over contracts.ts ('bounced' exists only client-side;
/// 'failed'/'refunded' only server-side — resolved API-CONTRACT §6, Q-list).
/// </summary>
[JsonConverter(typeof(EnumMemberJsonConverter<PaymentStatus>))]
public enum PaymentStatus
{
    [EnumMember(Value = "pending")]  Pending,
    [EnumMember(Value = "success")]  Success,
    [EnumMember(Value = "failed")]   Failed,
    [EnumMember(Value = "refunded")] Refunded,
}

// ─────────────────────────────────────────────────────────────────────────────

public class FeeStructure : TenantEntity
{
    public string Name { get; set; } = "";
    public string AcademicYear { get; set; } = "";
    public string Class { get; set; } = "";

    /// <summary>Null/blank = all sections.</summary>
    public string? Section { get; set; }

    /// <summary>Uppercased via ValueConverter, like Subject.Code.</summary>
    public string Currency { get; set; } = "INR";

    public ICollection<FeeHead> Heads { get; set; } = [];
    public ICollection<FeeInstallment> Installments { get; set; } = [];

    /// <summary>Soft-RETIRE (not soft-delete): list endpoints filter it, clone/
    /// reactivate flip it. Distinct from ISoftDeletable on purpose.</summary>
    public bool IsActive { get; set; } = true;

    // ── Audit trail ───────────────────────────────────────────────────────
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUsername { get; set; }
    public DateTime? LastEditedAt { get; set; }
    public Guid? LastEditedByUserId { get; set; }
    public string? LastEditedByUsername { get; set; }

    /// <summary>
    /// JSONB, bounded to last 20 — Mongo {_id:false}, append-only display log.
    /// </summary>
    public List<FeeStructureEdit> EditHistory { get; set; } = [];

    // ── Domain (ports of the Mongoose methods + pre-save validation) ──────

    /// <summary>recordEdit(actor, summary), incl. the 20-entry bound.</summary>
    public void RecordEdit(Guid? userId, string? username, string summary)
    {
        EditHistory.Add(new FeeStructureEdit
        {
            At = DateTime.UtcNow,
            ByUserId = userId,
            ByUsername = username,
            Summary = summary,
        });

        if (EditHistory.Count > 20)
            EditHistory = EditHistory.Skip(EditHistory.Count - 20).ToList();

        LastEditedAt = DateTime.UtcNow;
        LastEditedByUserId = userId;
        LastEditedByUsername = username;
    }

    /// <summary>
    /// totalAmount(includeOptional) — the ANNUALISED total. Frequency
    /// multipliers verbatim: monthly×12, quarterly×4, half_yearly×2, else ×1.
    /// </summary>
    public decimal TotalAmount(bool includeOptional = false) =>
        Heads.Where(h => includeOptional || !h.IsOptional)
             .Sum(h => h.Amount * h.Frequency switch
             {
                 FeeFrequency.Monthly    => 12,
                 FeeFrequency.Quarterly  => 4,
                 FeeFrequency.HalfYearly => 2,
                 _                       => 1,
             });

    /// <summary>
    /// Port of the pre('save') validation — throws the same messages, because
    /// api.ts surfaces `error` verbatim in the toast. Write services call this
    /// before SaveChanges; the hook's updateOne blind spot is thereby closed.
    /// </summary>
    public void Validate()
    {
        if (Heads.Count == 0)
            throw new AppException("At least one fee head is required",
                400, ErrorCodes.ValidationError);

        if (Installments.Count > 0)
        {
            var sum = Installments.Sum(i => i.Percentage);
            if (Math.Abs(sum - 100m) > 0.01m)
                throw new AppException(
                    $"Installment percentages must sum to 100 (got {sum})",
                    400, ErrorCodes.ValidationError);

            var names = Installments
                .Select(i => (i.Name ?? "").Trim().ToLowerInvariant())
                .ToList();
            if (names.Distinct().Count() != names.Count)
                throw new AppException(
                    "Installment names must be unique within a structure",
                    400, ErrorCodes.ValidationError);
        }
    }
}

/// <summary>{_id: true} → child table with serialized _id. Invoice lines
/// reference heads BY NAME (snapshot), not FK — renames don't rewrite history.</summary>
public class FeeHead : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid FeeStructureId { get; set; }
    [JsonIgnore] public FeeStructure FeeStructure { get; set; } = null!;

    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public FeeFrequency Frequency { get; set; } = FeeFrequency.Annual;
    public bool IsOptional { get; set; }
    public string? Description { get; set; }
}

public class FeeInstallment : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid FeeStructureId { get; set; }
    [JsonIgnore] public FeeStructure FeeStructure { get; set; } = null!;

    public string Name { get; set; } = "";
    public DateOnly DueDate { get; set; }

    /// <summary>0.01–100; the SET must sum to 100 (FeeStructure.Validate).</summary>
    public decimal Percentage { get; set; }
}

/// <summary>JSONB element — plain POCO, no _id on the wire (Mongo {_id:false}).</summary>
public sealed class FeeStructureEdit
{
    public DateTime At { get; set; }
    public Guid? ByUserId { get; set; }
    public string? ByUsername { get; set; }
    public string? Summary { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class FeeInvoice : TenantEntity
{
    public Guid StudentId { get; set; }
    [JsonIgnore] public Student Student { get; set; } = null!;

    public Guid? FeeStructureId { get; set; }
    [JsonIgnore] public FeeStructure? FeeStructure { get; set; }

    /// <summary>e.g. INV-2025-26-0001. UNIQUE (school, invoiceNo).</summary>
    public string InvoiceNo { get; set; } = "";

    public string AcademicYear { get; set; } = "";

    // Snapshots — old invoices stay correct when the student changes class.
    public string? StudentName { get; set; }
    public string? StudentAdmNo { get; set; }
    public string? StudentClass { get; set; }
    public string? StudentSection { get; set; }

    /// <summary>'Term 1'; null for one-shot invoices.</summary>
    public string? InstallmentName { get; set; }

    public DateOnly DueDate { get; set; }

    public ICollection<FeeInvoiceLine> Lines { get; set; } = [];

    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public string? DiscountReason { get; set; }
    public decimal LateFee { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>
    /// STORED status — only pending|partial|paid|cancelled ever land here
    /// (CHECK-enforced). NOT serialized: the wire sees EffectiveStatus.
    /// </summary>
    [JsonIgnore]
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;

    public string? Notes { get; set; }

    /// <summary>
    /// The Mongoose 'balance' virtual — contracts.ts reads it, the UI renders
    /// '—' without it. Backed by a STORED GENERATED column so it cannot drift
    /// from total/amountPaid; mapped read-only in the configuration.
    /// </summary>
    [JsonPropertyName("balance")]
    public decimal Balance { get; private set; }

    // ── Domain ────────────────────────────────────────────────────────────

    /// <summary>
    /// The wire status, replacing the pre('save') recompute + the feeScheduler
    /// overdue pass in one derivation.
    ///
    /// Node's hook compared dueDate (UTC midnight) &lt; now — so an invoice goes
    /// overdue ON its due date, at 00:00 UTC (05:30 IST). `DueDate &lt;= today(UTC)`
    /// reproduces that flip exactly. Precedence verbatim: cancelled sticky,
    /// then paid (requires total &gt; 0), then partial, then overdue, then pending.
    /// </summary>
    [JsonPropertyName("status")]
    public InvoiceStatus EffectiveStatus =>
        Status == InvoiceStatus.Cancelled ? InvoiceStatus.Cancelled
        : AmountPaid >= Total && Total > 0 ? InvoiceStatus.Paid
        : AmountPaid > 0                   ? InvoiceStatus.Partial
        : DueDate <= DateOnly.FromDateTime(DateTime.UtcNow) ? InvoiceStatus.Overdue
        : InvoiceStatus.Pending;

    /// <summary>
    /// The write-side half: normalises the STORED status after any change to
    /// AmountPaid/Total. Call inside the same transaction as the payment write
    /// (API-CONTRACT §3.3 — amountPaid is a denormalised sum over payments).
    /// Never writes Overdue; that stays derived.
    /// </summary>
    public void RecomputeStoredStatus()
    {
        if (Status == InvoiceStatus.Cancelled) return;   // sticky

        Status = AmountPaid >= Total && Total > 0 ? InvoiceStatus.Paid
               : AmountPaid > 0                   ? InvoiceStatus.Partial
               : InvoiceStatus.Pending;
    }
}

/// <summary>{_id: false} in Mongo, but a CHILD TABLE anyway (SCHEMA-MAP §3.1):
/// /reports/collection groups by headName — JSONB would force
/// jsonb_array_elements into every report query. PK internal, not serialized.</summary>
public class FeeInvoiceLine
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid InvoiceId { get; set; }
    [JsonIgnore] public FeeInvoice Invoice { get; set; } = null!;

    public string HeadName { get; set; } = "";
    public decimal Amount { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// razorpaySignature is deliberately NOT a column — the Node model verified it
/// once and discarded it ("long-lived HMACs in DB add only risk").
/// razorpayPaymentId + RazorpayVerified is the reconciliation record.
/// </summary>
public class Payment : TenantEntity
{
    public Guid InvoiceId { get; set; }
    [JsonIgnore] public FeeInvoice Invoice { get; set; } = null!;

    public Guid StudentId { get; set; }
    [JsonIgnore] public Student Student { get; set; } = null!;

    public string ReceiptNo { get; set; } = "";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    // Razorpay — plaintext by design (dedup + webhook idempotency need lookups,
    // and encrypted columns are opaque to SQL).
    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public bool RazorpayVerified { get; set; }

    // Offline instrument identifiers — ENCRYPTED AT REST (ValueConverter).
    public string? ChequeNo { get; set; }
    public string? ChequeBank { get; set; }
    public DateOnly? ChequeDate { get; set; }        // "not sensitive on its own"
    public string? TransactionRef { get; set; }      // UPI ref / NEFT trace

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? FailureReason { get; set; }

    public DateTime PaidAt { get; set; } = DateTime.UtcNow;

    /// <summary>Mongo field is `collectedBy` (no UserId suffix) — wire name pinned.</summary>
    [JsonPropertyName("collectedBy")]
    public Guid? CollectedByUserId { get; set; }
    [JsonIgnore] public User? CollectedByUser { get; set; }

    public string? Notes { get; set; }

    /// <summary>Client-generated UUID when the payment form opens — network
    /// retries can't double-create. Partial unique (school, invoice, key).</summary>
    public string? IdempotencyKey { get; set; }
}
