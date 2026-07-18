using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // NULLABLE — superadmin belongs to no school.
        b.Property(x => x.SchoolId).IsRequired(false);

        b.HasOne(x => x.School)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // citext — Mongo has `lowercase: true`. Parakh already hit this exact bug
        // (case-sensitive tenant/email lookups); don't hit it twice.
        b.Property(x => x.Username).HasColumnType("citext").IsRequired();
        b.Property(x => x.Email).HasColumnType("citext");

        b.Property(x => x.Password).IsRequired();
        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Role).HasColumnName("role");
        b.Property(x => x.IsActive).HasDefaultValue(true);

        // UNIQUE (school_id, username).
        //
        // ⚠️ school_id is NULLABLE and in Postgres NULL != NULL — so this does NOT
        // constrain superadmins: two superadmins could both be 'admin'. Mongo had
        // the same hole. A second partial index closes it.
        b.HasIndex(x => new { x.SchoolId, x.Username })
            .IsUnique()
            .HasDatabaseName("uq_users_school_username");

        b.HasIndex(x => x.Username)
            .IsUnique()
            .HasFilter("school_id IS NULL")
            .HasDatabaseName("uq_users_global_username");

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // parentOf[] → join table. Drives parent row-scoping (API-CONTRACT §1.1).
        b.HasMany(x => x.ParentOf)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "user_parent_of",
                r => r.HasOne<Student>().WithMany()
                      .HasForeignKey("student_id").OnDelete(DeleteBehavior.Cascade),
                l => l.HasOne<User>().WithMany()
                      .HasForeignKey("user_id").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("user_id", "student_id");
                    // Read on EVERY parent request — GET /api/students filters to
                    // this set. The PK covers user_id; this covers the reverse.
                    j.HasIndex("student_id");
                });

        b.HasMany(x => x.RefreshTokens)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        // Computed, not stored.
        b.Ignore(x => x.FullName);
        b.Ignore(x => x.ParentOfIds);
        b.Ignore(x => x.IsSuperAdmin);
    }
}

public sealed class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> b)
    {
        b.ToTable("user_refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.TokenHash).IsRequired();

        // Every authenticated refresh looks a token up by its hash.
        b.HasIndex(x => x.TokenHash).HasDatabaseName("ix_refresh_hash");

        b.HasIndex(x => x.UserId).HasDatabaseName("ix_refresh_user");

        // Supports the DELETE WHERE expires_at < now() cleanup job.
        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_refresh_expiry");

        // inet handles IPv4 and IPv6 without a length guess.
        b.Property(x => x.Ip).HasColumnType("inet");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        b.Ignore(x => x.IsExpired);
    }
}
