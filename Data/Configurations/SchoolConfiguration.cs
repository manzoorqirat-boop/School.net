using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Crypto;

namespace QMSoft.Api.Data.Configurations;

public sealed class SchoolConfiguration : IEntityTypeConfiguration<School>
{
    private readonly ICryptoService _crypto;

    public SchoolConfiguration(ICryptoService crypto) => _crypto = crypto;

    public void Configure(EntityTypeBuilder<School> b)
    {
        b.ToTable("schools");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();

        // citext: slug is `lowercase: true` in Mongo. Postgres is case-sensitive,
        // so a lookup for 'DPS' would miss a stored 'dps'.
        b.Property(x => x.Slug).HasColumnType("citext").IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();

        b.Property(x => x.Email).HasColumnType("citext");

        b.Property(x => x.PrimaryColor).HasDefaultValue("#1e40af");
        b.Property(x => x.AcademicYear).HasDefaultValue("2025-2026");

        b.Property(x => x.AcademicYearStartMonth).HasDefaultValue(4);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_schools_ay_start_month",
            "\"AcademicYearStartMonth\" BETWEEN 1 AND 12"));

        // text[] — never queried individually (SCHEMA-MAP §14.7).
        b.Property(x => x.Classes).HasColumnType("text[]");
        b.Property(x => x.Sections).HasColumnType("text[]");
        b.Property(x => x.WorkingDays).HasColumnType("text[]");

        // Mongo enum on workingDays: 'Mon'..'Sun'. A native array can't carry an
        // element-level enum constraint, so express it as a CHECK.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_schools_working_days",
            "\"WorkingDays\" <@ ARRAY['Mon','Tue','Wed','Thu','Fri','Sat','Sun']::text[]"));

        // 1..28 — capped so the day exists in February (per the Mongo comment).
        b.Property(x => x.FeeBillingDay).HasDefaultValue(1);
        b.Property(x => x.FeeReminderDay).HasDefaultValue(10);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_schools_fee_days",
            "\"FeeBillingDay\" BETWEEN 1 AND 28 AND \"FeeReminderDay\" BETWEEN 1 AND 28"));

        b.Property(x => x.LeaveRequireApproval).HasDefaultValue(true);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.Property(x => x.Type).HasColumnName("type");
        b.Property(x => x.Plan).HasColumnName("plan");

        // AES-256-GCM at rest, enc:v1: wire format.
        //
        // NOTE: a converted column is opaque to SQL — no WHERE, no index, no
        // ILIKE. Nothing queries this, which is why the converter is safe here.
        b.Property(x => x.RazorpayKeySecret)
            .HasConversion(new ValueConverter<string?, string?>(
                v => _crypto.Encrypt(v),
                v => _crypto.Decrypt(v)));

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        b.HasMany(x => x.LeaveTypes)
            .WithOne(x => x.School)
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SchoolLeaveTypeConfiguration : IEntityTypeConfiguration<SchoolLeaveType>
{
    public void Configure(EntityTypeBuilder<SchoolLeaveType> b)
    {
        b.ToTable("school_leave_types");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();

        // numeric(5,1): half-days are real (0.5). A smallint would silently floor.
        b.Property(x => x.TotalDays).HasColumnType("numeric(5,1)").HasDefaultValue(0m);
        b.Property(x => x.IsPaid).HasDefaultValue(true);

        b.ToTable(t => t.HasCheckConstraint("ck_school_leave_types_days", "\"TotalDays\" >= 0"));

        // Leave.types is seeded from this by name, and leave_records join by name.
        b.HasIndex(x => new { x.SchoolId, x.Name }).IsUnique();
    }
}
