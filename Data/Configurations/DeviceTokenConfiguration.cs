using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Token).IsRequired();
        b.Property(x => x.IsRevoked).HasDefaultValue(false);
        b.Property(x => x.LastSeenAt).HasDefaultValueSql("now()");

        // GLOBALLY unique, not unique per user.
        //
        // A device belongs to whoever is signed in on it. When a parent hands
        // the tablet to their spouse and they sign in, the same token must MOVE
        // to the new user, not exist twice — otherwise the first parent keeps
        // receiving notices meant for the second. Registration upserts on this
        // key and reassigns UserId.
        b.HasIndex(x => x.Token).IsUnique().HasDatabaseName("ux_device_tokens_token");

        // The send path's access pattern: live tokens for a set of users.
        b.HasIndex(x => new { x.SchoolId, x.UserId, x.IsRevoked })
            .HasDatabaseName("ix_device_tokens_school_user");

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}
