using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class TimeSlotConfiguration : IEntityTypeConfiguration<TimeSlot>
{
    public void Configure(EntityTypeBuilder<TimeSlot> b)
    {
        b.ToTable("time_slots");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.DefaultStartTime).IsRequired();
        b.Property(x => x.DefaultEndTime).IsRequired();
        b.Property(x => x.SpansMultiplePeriods).HasDefaultValue(false);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        // dayTimes → JSONB: tiny per-day override list, never queried by element.
        b.Property(x => x.DayTimes)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<SlotDayTime>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<List<SlotDayTime>>(
                    (a, z) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                           == JsonSerializer.Serialize(z, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<SlotDayTime>>(
                            JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                            (JsonSerializerOptions?)null)!));

        b.HasIndex(x => new { x.SchoolId, x.SlotNumber })
            .IsUnique()
            .HasDatabaseName("uq_time_slots_school_number");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class TimetableConfiguration : IEntityTypeConfiguration<Timetable>
{
    public void Configure(EntityTypeBuilder<Timetable> b)
    {
        b.ToTable("timetables");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Class).IsRequired();
        b.Property(x => x.Section).IsRequired();
        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.FromDate).HasColumnType("date").IsRequired();
        b.Property(x => x.ToDate).HasColumnType("date");
        b.Property(x => x.Status).HasColumnName("status");

        b.ToTable(t => t.HasCheckConstraint(
            "ck_timetables_dates", "\"ToDate\" IS NULL OR \"ToDate\" >= \"FromDate\""));

        // Mongo's lookup index verbatim — ?date=YYYY-MM-DD resolution scans
        // (class, section, year) then filters the effective window.
        b.HasIndex(x => new { x.SchoolId, x.Class, x.Section, x.AcademicYear, x.FromDate })
            .HasDatabaseName("ix_timetables_lookup");

        b.HasMany(x => x.Entries)
            .WithOne(x => x.Timetable)
            .HasForeignKey(x => x.TimetableId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class TimetableEntryConfiguration : IEntityTypeConfiguration<TimetableEntry>
{
    public void Configure(EntityTypeBuilder<TimetableEntry> b)
    {
        b.ToTable("timetable_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Subject)
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.DayOfWeek).HasColumnType("smallint");
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_timetable_entries_dow", "\"DayOfWeek\" BETWEEN 0 AND 6"));

        // Grid render: one query per timetable, ordered by (day, slot).
        b.HasIndex(x => new { x.TimetableId, x.DayOfWeek, x.SlotNumber })
            .HasDatabaseName("ix_timetable_entries_grid");

        // Teacher workload view (GET /timetables/teacher/:teacherId) joins
        // entries by teacher across timetables.
        b.HasIndex(x => x.TeacherId).HasDatabaseName("ix_timetable_entries_teacher");

        // NOTE — deliberately NO unique on (timetableId, dayOfWeek, slotNumber):
        // Mongo had none, and the copy-day / copy-from flows in the controller
        // legitimately stage duplicate cells before the user resolves them.
        // Clash detection is a UI concern here, not a constraint.
    }
}

public sealed class TimetableVariationConfiguration : IEntityTypeConfiguration<TimetableVariation>
{
    public void Configure(EntityTypeBuilder<TimetableVariation> b)
    {
        b.ToTable("timetable_variations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Timetable)
            .WithMany()
            .HasForeignKey(x => x.TimetableId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Subject)
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Property(x => x.Date).HasColumnType("date").IsRequired();
        b.Property(x => x.DayOfWeek).HasColumnType("smallint");
        b.Property(x => x.Type).HasColumnName("type");

        // The Date setter keeps DayOfWeek in sync; EF materialisation goes
        // through the same setter, so a stored row re-derives consistently.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_timetable_variations_dow", "\"DayOfWeek\" BETWEEN 0 AND 6"));

        // One override per (timetable, date, slot) — all NOT NULL, plain
        // unique ports faithfully.
        b.HasIndex(x => new { x.SchoolId, x.TimetableId, x.Date, x.SlotNumber })
            .IsUnique()
            .HasDatabaseName("uq_timetable_variations_key");

        // GET /:id/variations?date= — day view resolution.
        b.HasIndex(x => new { x.SchoolId, x.Date })
            .HasDatabaseName("ix_timetable_variations_date");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}
