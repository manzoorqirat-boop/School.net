using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Crypto;

namespace QMSoft.Api.Data.Configurations;

internal static class Jsonb
{
    /// <summary>Shared JSONB list mapping — serialize/compare by JSON string.</summary>
    public static void MapList<TOwner, TItem>(
        EntityTypeBuilder<TOwner> b,
        System.Linq.Expressions.Expression<Func<TOwner, List<TItem>>> prop)
        where TOwner : class
    {
        b.Property(prop)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<TItem>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<List<TItem>>(
                    (a, z) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                           == JsonSerializer.Serialize(z, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<TItem>>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null)!));
    }
}

public sealed class SalaryStructureConfiguration : IEntityTypeConfiguration<SalaryStructure>
{
    private readonly ICryptoService _crypto;
    public SalaryStructureConfiguration(ICryptoService crypto) => _crypto = crypto;

    public void Configure(EntityTypeBuilder<SalaryStructure> b)
    {
        b.ToTable("salary_structures");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnType("date").IsRequired();
        b.Property(x => x.EffectiveTo).HasColumnType("date");

        // ── PERCENT columns: numeric(5,2). RUPEE columns: numeric(12,2). ──
        // Same names as payrolls but different meaning — the column types are
        // the guard rail (SCHEMA-MAP §5.1: HRA "40" must be 40%, not ₹40).
        b.Property(x => x.BaseSalary).HasColumnType("numeric(12,2)");        // ₹
        b.Property(x => x.Da).HasColumnType("numeric(5,2)").HasDefaultValue(0m);   // %
        b.Property(x => x.Hra).HasColumnType("numeric(5,2)").HasDefaultValue(0m);  // %
        b.Property(x => x.Ta).HasColumnType("numeric(12,2)").HasDefaultValue(0m);  // ₹
        b.Property(x => x.Pf).HasColumnType("numeric(5,2)").HasDefaultValue(0m);   // %
        b.Property(x => x.Esi).HasColumnType("numeric(5,2)").HasDefaultValue(0m);  // %
        b.Property(x => x.ProfessionalTax).HasColumnType("numeric(12,2)").HasDefaultValue(0m); // ₹
        b.Property(x => x.IncomeTax).HasColumnType("numeric(5,2)").HasDefaultValue(0m);        // % of GROSS
        b.Property(x => x.LeaveDeductionPerDay).HasColumnType("numeric(12,2)").HasDefaultValue(0m);

        b.ToTable(t => t.HasCheckConstraint("ck_salary_structures_pcts",
            "\"BaseSalary\" >= 0 AND \"Da\" BETWEEN 0 AND 200 AND \"Hra\" BETWEEN 0 AND 200 AND " +
            "\"Ta\" >= 0 AND \"Pf\" BETWEEN 0 AND 100 AND \"Esi\" BETWEEN 0 AND 100 AND " +
            "\"ProfessionalTax\" >= 0 AND \"IncomeTax\" BETWEEN 0 AND 100 AND " +
            "\"LeaveDeductionPerDay\" >= 0"));

        Jsonb.MapList(b, x => x.OtherAllowances);
        Jsonb.MapList(b, x => x.OtherDeductions);

        // ── ENCRYPTED bank fields ─────────────────────────────────────────
        // BankIfsc: the entity setter uppercases FIRST, this converter encrypts
        // SECOND — canonical form is what's stored, matching the Node model's
        // uppercase-before-encrypt design.
        var enc = new ValueConverter<string?, string?>(
            v => _crypto.Encrypt(v), v => _crypto.Decrypt(v));
        b.Property(x => x.BankAccountNumber).HasConversion(enc);
        b.Property(x => x.BankIfsc).HasConversion(enc);
        b.Property(x => x.BankAccountHolder).HasConversion(enc);

        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.HasIndex(x => new { x.SchoolId, x.TeacherId, x.AcademicYear })
            .IsUnique()
            .HasDatabaseName("uq_salary_structures_teacher_year");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class PayrollConfiguration : IEntityTypeConfiguration<Payroll>
{
    private readonly ICryptoService _crypto;
    public PayrollConfiguration(ICryptoService crypto) => _crypto = crypto;

    public void Configure(EntityTypeBuilder<Payroll> b)
    {
        b.ToTable("payrolls");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.PayrollRun)
            .WithMany()
            .HasForeignKey(x => x.PayrollRunId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.SalaryStructure)
            .WithMany()
            .HasForeignKey(x => x.SalaryStructureId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── EVERYTHING is ₹ here — numeric(12,2) across the board. ────────
        foreach (var col in new[] { nameof(Payroll.BaseSalary), nameof(Payroll.Da),
            nameof(Payroll.Hra), nameof(Payroll.Ta), nameof(Payroll.OtherAllowances),
            nameof(Payroll.GrossSalary), nameof(Payroll.Pf), nameof(Payroll.Esi),
            nameof(Payroll.ProfessionalTax), nameof(Payroll.IncomeTax),
            nameof(Payroll.LeaveDeduction), nameof(Payroll.OtherDeductions),
            nameof(Payroll.TotalDeductions), nameof(Payroll.NetSalary),
            nameof(Payroll.DailyRate) })
        {
            b.Property(col).HasColumnType("numeric(12,2)");
        }

        b.Property(x => x.UnpaidLeaveDays).HasColumnType("numeric(5,1)");   // half-days

        b.ToTable(t => t.HasCheckConstraint("ck_payrolls_nonneg",
            "\"BaseSalary\" >= 0 AND \"GrossSalary\" >= 0 AND \"TotalDeductions\" >= 0 AND " +
            "\"NetSalary\" >= 0 AND \"UnpaidLeaveDays\" >= 0 AND \"DailyRate\" >= 0"));
        b.ToTable(t => t.HasCheckConstraint("ck_payrolls_period",
            "\"Month\" BETWEEN 1 AND 12 AND \"Year\" BETWEEN 2000 AND 2100"));

        Jsonb.MapList(b, x => x.OtherAllowanceItems);
        Jsonb.MapList(b, x => x.OtherDeductionItems);

        var enc = new ValueConverter<string?, string?>(
            v => _crypto.Encrypt(v), v => _crypto.Decrypt(v));
        b.Property(x => x.BankAccount).HasConversion(enc);
        b.Property(x => x.BankIfsc).HasConversion(enc);
        b.Property(x => x.BankAccountHolder).HasConversion(enc);

        b.Property(x => x.Status).HasColumnName("status");

        // One payslip per teacher per month.
        b.HasIndex(x => new { x.SchoolId, x.TeacherId, x.Year, x.Month })
            .IsUnique()
            .HasDatabaseName("uq_payrolls_teacher_period");

        b.HasIndex(x => new { x.SchoolId, x.Status, x.Year, x.Month })
            .HasDatabaseName("ix_payrolls_status_period");

        b.HasIndex(x => new { x.SchoolId, x.PayrollRunId })
            .HasDatabaseName("ix_payrolls_run");

        b.Ignore(x => x.PeriodLabel);   // computed, wire-only

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("payroll_runs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Status).HasColumnName("status");

        b.Property(x => x.TotalGross).HasColumnType("numeric(14,2)");
        b.Property(x => x.TotalDeductions).HasColumnType("numeric(14,2)");
        b.Property(x => x.TotalNet).HasColumnType("numeric(14,2)");

        b.ToTable(t => t.HasCheckConstraint("ck_payroll_runs_period",
            "\"Month\" BETWEEN 1 AND 12 AND \"Year\" BETWEEN 2000 AND 2100"));

        Jsonb.MapList(b, x => x.Skipped);

        b.HasIndex(x => new { x.SchoolId, x.Year, x.Month })
            .IsUnique()
            .HasDatabaseName("uq_payroll_runs_period");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class LeaveConfiguration : IEntityTypeConfiguration<Leave>
{
    public void Configure(EntityTypeBuilder<Leave> b)
    {
        b.ToTable("leaves");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.AcademicYear).IsRequired();

        b.HasIndex(x => new { x.SchoolId, x.TeacherId, x.AcademicYear })
            .IsUnique()
            .HasDatabaseName("uq_leaves_teacher_year");

        b.HasMany(x => x.Types)
            .WithOne(x => x.Leave)
            .HasForeignKey(x => x.LeaveId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Records)
            .WithOne(x => x.Leave)
            .HasForeignKey(x => x.LeaveId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.ToTable("leave_types");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.TotalDays).HasColumnType("numeric(5,1)").HasDefaultValue(0m);
        b.Property(x => x.UsedDays).HasColumnType("numeric(5,1)").HasDefaultValue(0m);
        b.Property(x => x.IsPaid).HasDefaultValue(true);

        // The pre('save') balance loop → a STORED GENERATED column.
        b.Property(x => x.BalanceDays)
            .HasColumnType("numeric(5,1)")
            .HasComputedColumnSql("total_days - used_days", stored: true);

        // Name-uniqueness makes the legacy name-join at least unambiguous
        // (SCHEMA-MAP §5.3).
        b.HasIndex(x => new { x.LeaveId, x.Name })
            .IsUnique()
            .HasDatabaseName("uq_leave_types_name");
    }
}

public sealed class LeaveRecordConfiguration : IEntityTypeConfiguration<LeaveRecord>
{
    public void Configure(EntityTypeBuilder<LeaveRecord> b)
    {
        b.ToTable("leave_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Type).IsRequired();
        b.Property(x => x.FromDate).HasColumnType("date");
        b.Property(x => x.ToDate).HasColumnType("date");
        b.Property(x => x.Days).HasColumnType("numeric(5,1)");
        b.Property(x => x.Status).HasColumnName("status");

        b.ToTable(t => t.HasCheckConstraint("ck_leave_records_dates", "\"ToDate\" >= \"FromDate\""));

        // The latent-bug fix: FK to the type. SET NULL on type deletion —
        // the record survives with its display-name snapshot.
        b.HasOne<LeaveType>()
            .WithMany()
            .HasForeignKey(x => x.LeaveTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.LeaveId);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
    }
}

/// <summary>
/// The STRUCTURAL half of safeUpsertForGeneration — raw SQL for the initial
/// migration (EF fluent can't express OLD/NEW triggers). Blocks any data
/// modification of a locked/paid payslip that isn't an explicit status
/// transition, no matter which code path issues the UPDATE.
///
/// Legal transitions through the guard:
///   locked → paid | generated (unlock)     paid → (nothing)
/// Everything else on a locked/paid row raises P0001 → mapped to 409
/// DUPLICATE_ERROR by ExceptionHandlingMiddleware.
///
/// Usage in the migration:  migrationBuilder.Sql(PayrollTriggerSql.UpAll);
/// </summary>
public static class PayrollTriggerSql
{
    public const string UpAll = @"
CREATE OR REPLACE FUNCTION guard_payroll_immutable() RETURNS trigger AS $$
BEGIN
  IF OLD.status = 'paid' AND NEW.status = 'paid' THEN
    RAISE EXCEPTION 'Payslip % is paid and cannot be modified', OLD.id;
  END IF;
  IF OLD.status = 'locked' AND NEW.status = 'locked' THEN
    RAISE EXCEPTION 'Payslip % is locked and cannot be modified', OLD.id;
  END IF;
  RETURN NEW;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER trg_payrolls_immutable
  BEFORE UPDATE ON payrolls
  FOR EACH ROW EXECUTE FUNCTION guard_payroll_immutable();

CREATE OR REPLACE FUNCTION guard_payroll_no_delete() RETURNS trigger AS $$
BEGIN
  IF OLD.status IN ('locked','paid') THEN
    RAISE EXCEPTION 'Payslip % is % and cannot be deleted', OLD.id, OLD.status;
  END IF;
  RETURN OLD;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER trg_payrolls_no_delete
  BEFORE DELETE ON payrolls
  FOR EACH ROW EXECUTE FUNCTION guard_payroll_no_delete();
";

    public const string DownAll = @"
DROP TRIGGER IF EXISTS trg_payrolls_immutable ON payrolls;
DROP TRIGGER IF EXISTS trg_payrolls_no_delete ON payrolls;
DROP FUNCTION IF EXISTS guard_payroll_immutable();
DROP FUNCTION IF EXISTS guard_payroll_no_delete();
";
}
