using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class NoticeConfiguration : IEntityTypeConfiguration<Notice>
{
    public void Configure(EntityTypeBuilder<Notice> b)
    {
        b.ToTable("notices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Title).IsRequired();
        b.Property(x => x.Body).IsRequired();

        // Native enum type `notice_priority` — registered in AppDbContext
        // (HasPostgresEnum) and mapped in Program.cs (dsb.MapEnum).
        b.Property(x => x.Priority).HasColumnName("priority");

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
