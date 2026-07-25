using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class NoticeConfiguration : IEntityTypeConfiguration<Notice>
{
    /// <summary>
    /// CLR enum ↔ wire string. Written out explicitly rather than derived from
    /// the [EnumMember] attributes: these three literals must match
    /// ck_notices_priority exactly, and a mapping that is generated from
    /// attribute metadata can drift from the constraint without anything
    /// failing until a write hits the database.
    /// </summary>
    private static readonly ValueConverter<NoticePriority, string> PriorityConverter =
        new(
            v => v == NoticePriority.Urgent    ? "urgent"
               : v == NoticePriority.Important ? "important"
               :                                 "normal",
            v => v == "urgent"    ? NoticePriority.Urgent
               : v == "important" ? NoticePriority.Important
               :                    NoticePriority.Normal);

    public void Configure(EntityTypeBuilder<Notice> b)
    {
        b.ToTable("notices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Title).IsRequired();
        b.Property(x => x.Body).IsRequired();

        // Stored as TEXT with a CHECK, not a native Postgres enum.
        //
        // The CLR enum stays — the wire contract ("normal"/"important"/
        // "urgent") and NoticePriority in C# are unchanged — but the column is
        // plain text and the value converter below does the translation.
        //
        // Native enums have now broken this schema twice: `status <> 3` on
        // fee_invoices (an int comparison against an enum column, which killed
        // CREATE TABLE) and notice_priority (an InvalidCastException on write
        // whenever the Npgsql type mapping is not perfectly in step with the
        // database). Both failures need THREE things to agree — HasPostgresEnum
        // in AppDbContext, dsb.MapEnum in Program.cs, and the CREATE TYPE
        // actually having run — and a mismatch surfaces only at runtime, on
        // write, as an error that names neither the column nor the type.
        //
        // A text column with a CHECK gives the same integrity, is validated by
        // the database in one place, and cannot fail this way.
        b.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasColumnType("text")
            .HasConversion(PriorityConverter);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_notices_priority",
            "priority IN ('normal', 'important', 'urgent')"));

        b.Property(x => x.IsPinned).HasDefaultValue(false);
        b.Property(x => x.IsDeleted).HasDefaultValue(false);

        b.Property(x => x.TargetRoles).HasColumnType("text[]");
        b.Property(x => x.TargetClasses).HasColumnType("text[]");

        // Same label set as ck_polls_target_roles. An EMPTY array satisfies <@
        // (the empty set is a subset of everything), which is what makes
        // "empty = all roles" expressible without a nullable column.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_notices_target_roles",
            "target_roles <@ ARRAY['parent','teacher','student'," +
            "'school_admin','principal','accountant']::text[]"));

        // Guards the one ordering invariant the UI depends on.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_notices_window",
            "expires_at IS NULL OR publish_at IS NULL OR expires_at > publish_at"));

        b.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // The board's exact access path: per-school, pinned first, newest first.
        b.HasIndex(x => new { x.SchoolId, x.IsPinned, x.CreatedAt })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_notices_school_pinned_time");

        // role/class targeting is `target_roles && ARRAY[...]` containment.
        b.HasIndex(x => x.TargetRoles)
            .HasMethod("gin")
            .HasDatabaseName("ix_notices_target_roles");
        b.HasIndex(x => x.TargetClasses)
            .HasMethod("gin")
            .HasDatabaseName("ix_notices_target_classes");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}
