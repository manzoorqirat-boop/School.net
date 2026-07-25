using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Crypto;

namespace QMSoft.Api.Data.Configurations;

public sealed class FeeStructureConfiguration : IEntityTypeConfiguration<FeeStructure>
{
    public void Configure(EntityTypeBuilder<FeeStructure> b)
    {
        b.ToTable("fee_structures");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired().HasMaxLength(150);
        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.Class).IsRequired();

        // uppercase: true — ValueConverter, same pattern as Subject.Code.
        b.Property(x => x.Currency)
            .HasMaxLength(3)
            .HasDefaultValue("INR")
            .HasConversion(v => v.ToUpperInvariant(), v => v);

        b.Property(x => x.IsActive).HasDefaultValue(true);

        // editHistory → JSONB. Bounded to 20 by RecordEdit; never queried.
        b.Property(x => x.EditHistory)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<FeeStructureEdit>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<List<FeeStructureEdit>>(
                    (a, z) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                           == JsonSerializer.Serialize(z, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<FeeStructureEdit>>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null)!));

        b.HasIndex(x => new { x.SchoolId, x.AcademicYear, x.Class, x.IsActive })
            .HasDatabaseName("ix_fee_structures_lookup");

        b.HasMany(x => x.Heads)
            .WithOne(x => x.FeeStructure)
            .HasForeignKey(x => x.FeeStructureId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Installments)
            .WithOne(x => x.FeeStructure)
            .HasForeignKey(x => x.FeeStructureId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class FeeHeadConfiguration : IEntityTypeConfiguration<FeeHead>
{
    public void Configure(EntityTypeBuilder<FeeHead> b)
    {
        b.ToTable("fee_heads");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Amount).HasColumnType("numeric(12,2)");   // MONEY
        b.Property(x => x.Frequency).HasColumnName("frequency");
        b.Property(x => x.IsOptional).HasDefaultValue(false);
        b.Property(x => x.Description).HasMaxLength(300);

        b.ToTable(t => t.HasCheckConstraint("ck_fee_heads_amount", "amount >= 0"));
        b.HasIndex(x => x.FeeStructureId);
    }
}

public sealed class FeeInstallmentConfiguration : IEntityTypeConfiguration<FeeInstallment>
{
    public void Configure(EntityTypeBuilder<FeeInstallment> b)
    {
        b.ToTable("fee_installments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired().HasMaxLength(50);
        b.Property(x => x.DueDate).HasColumnType("date");

        // A PERCENT of the structure total — numeric(5,2), NOT money.
        b.Property(x => x.Percentage).HasColumnType("numeric(5,2)");
        b.ToTable(t => t.HasCheckConstraint(
            "ck_fee_installments_pct", "percentage > 0 AND percentage <= 100"));

        // The Mongoose hook's case-insensitive name-uniqueness, made structural.
        // (The sum-to-100 rule spans rows and stays in FeeStructure.Validate().)
        b.HasIndex(x => new { x.FeeStructureId, x.Name })
            .IsUnique()
            .HasDatabaseName("uq_fee_installments_name");

        b.HasIndex(x => x.FeeStructureId);
    }
}

public sealed class FeeInvoiceConfiguration : IEntityTypeConfiguration<FeeInvoice>
{
    public void Configure(EntityTypeBuilder<FeeInvoice> b)
    {
        b.ToTable("fee_invoices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FeeStructure)
            .WithMany()
            .HasForeignKey(x => x.FeeStructureId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.InvoiceNo).IsRequired();
        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.DueDate).HasColumnType("date").IsRequired();

        // ── MONEY — all numeric(12,2) ─────────────────────────────────────
        b.Property(x => x.Subtotal).HasColumnType("numeric(12,2)");
        b.Property(x => x.Discount).HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.LateFee).HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.Total).HasColumnType("numeric(12,2)");
        b.Property(x => x.AmountPaid).HasColumnType("numeric(12,2)").HasDefaultValue(0m);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_fee_invoices_amounts",
            "subtotal >= 0 AND discount >= 0 AND late_fee >= 0 AND " +
            "total >= 0 AND amount_paid >= 0"));

        // The 'balance' virtual → STORED GENERATED column. Cannot drift; the
        // frontend reads it (contracts.ts marks it optional so TS would never
        // catch its absence — the UI would just render '—').
        b.Property(x => x.Balance)
            .HasColumnType("numeric(12,2)")
            .HasComputedColumnSql("GREATEST(0::numeric, total - amount_paid)", stored: true);

        b.Property(x => x.Status).HasColumnName("status");

        // 'overdue' exists in the DB enum for wire parity but must NEVER be
        // stored — it's derived (EffectiveStatus). Structural, not conventional.
        //
        // Compared against the ENUM LABEL, not an integer. Program.cs registers
        // dsb.MapEnum<InvoiceStatus>("invoice_status", …), so this column is the
        // Postgres enum type `invoice_status` — not a smallint. The previous
        // `status <> 3` was rejected at CREATE TABLE with
        //   42883: operator does not exist: invoice_status <> integer
        // which killed the whole fee_invoices statement. Because the sibling
        // tables (fee_structures/heads/installments) carry no such constraint
        // they were created normally, so the schema looked fine right up until
        // the first request touching invoices died with 42P01.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_fee_invoices_no_stored_overdue", "status <> 'overdue'::invoice_status"));

        b.HasIndex(x => new { x.SchoolId, x.InvoiceNo })
            .IsUnique()
            .HasDatabaseName("uq_fee_invoices_school_no");

        b.HasIndex(x => new { x.SchoolId, x.StudentId, x.AcademicYear })
            .HasDatabaseName("ix_fee_invoices_student_year");

        b.HasIndex(x => new { x.SchoolId, x.DueDate, x.Status })
            .HasDatabaseName("ix_fee_invoices_due_status");

        b.HasMany(x => x.Lines)
            .WithOne(x => x.Invoice)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.EffectiveStatus);   // computed, wire-only

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class FeeInvoiceLineConfiguration : IEntityTypeConfiguration<FeeInvoiceLine>
{
    public void Configure(EntityTypeBuilder<FeeInvoiceLine> b)
    {
        b.ToTable("fee_invoice_lines");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.HeadName).IsRequired();
        b.Property(x => x.Amount).HasColumnType("numeric(12,2)");
        b.ToTable(t => t.HasCheckConstraint("ck_fee_invoice_lines_amount", "amount >= 0"));

        // /reports/collection groups by (school, headName) via a join to
        // invoices; the FK index is what keeps that join cheap.
        b.HasIndex(x => x.InvoiceId);
        b.HasIndex(x => x.HeadName).HasDatabaseName("ix_fee_invoice_lines_head");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private readonly ICryptoService _crypto;

    public PaymentConfiguration(ICryptoService crypto) => _crypto = crypto;

    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Invoice)
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CollectedByUser)
            .WithMany()
            .HasForeignKey(x => x.CollectedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Property(x => x.ReceiptNo).IsRequired();
        b.Property(x => x.Amount).HasColumnType("numeric(12,2)");
        b.ToTable(t => t.HasCheckConstraint("ck_payments_amount", "amount >= 0"));

        b.Property(x => x.Method).HasColumnName("method");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.RazorpayVerified).HasDefaultValue(false);
        b.Property(x => x.ChequeDate).HasColumnType("date");

        // ── ENCRYPTED AT REST (AES-256-GCM, enc:v1:) ──────────────────────
        // Opaque to SQL afterwards — no WHERE, no index. Nothing queries them.
        var enc = new ValueConverter<string?, string?>(
            v => _crypto.Encrypt(v), v => _crypto.Decrypt(v));
        b.Property(x => x.ChequeNo).HasConversion(enc);
        b.Property(x => x.ChequeBank).HasConversion(enc);
        b.Property(x => x.TransactionRef).HasConversion(enc);
        // razorpaySignature: NO COLUMN, by design — verified once, discarded.

        b.HasIndex(x => new { x.SchoolId, x.ReceiptNo })
            .IsUnique()
            .HasDatabaseName("uq_payments_receipt");

        b.HasIndex(x => new { x.SchoolId, x.PaidAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_payments_paid");

        b.HasIndex(x => x.RazorpayOrderId).HasDatabaseName("ix_payments_rzp_order");

        // ── THE TWO PARTIAL UNIQUES — webhook & offline idempotency ───────
        // Mongo sparse/partialFilterExpression → WHERE clauses. Flattening
        // either to a plain unique makes it useless: every offline payment has
        // razorpay_payment_id NULL, and NULL != NULL means the constraint
        // never fires (SCHEMA-MAP §3.2). uq_payments_rzp is what makes a
        // Razorpay webhook RETRY hit 23505 instead of double-crediting.
        b.HasIndex(x => x.RazorpayPaymentId)
            .IsUnique()
            .HasFilter("razorpay_payment_id IS NOT NULL")
            .HasDatabaseName("uq_payments_rzp");

        b.HasIndex(x => new { x.SchoolId, x.InvoiceId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("uq_payments_idem");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}
