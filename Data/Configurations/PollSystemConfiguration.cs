using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class PollConfiguration : IEntityTypeConfiguration<Poll>
{
    public void Configure(EntityTypeBuilder<Poll> b)
    {
        b.ToTable("polls");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Title).IsRequired();
        b.Property(x => x.Category).HasColumnName("category");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.ShowResultsBeforeClose).HasDefaultValue(true);
        b.Property(x => x.AllowAnonymous).HasDefaultValue(false);

        // Mongo's targetRoles element enum → array containment CHECK.
        // superadmin deliberately absent, same as the Mongo enum.
        b.Property(x => x.TargetRoles).HasColumnType("text[]");
        b.ToTable(t => t.HasCheckConstraint(
            "ck_polls_target_roles",
            "target_roles <@ ARRAY['parent','teacher','student'," +
            "'school_admin','principal','accountant']::text[]"));

        b.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.SchoolId, x.Status })
            .HasDatabaseName("ix_polls_school_status");

        b.HasMany(x => x.Questions)
            .WithOne(x => x.Poll)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class PollQuestionConfiguration : IEntityTypeConfiguration<PollQuestion>
{
    public void Configure(EntityTypeBuilder<PollQuestion> b)
    {
        b.ToTable("poll_questions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Text).IsRequired();

        b.HasMany(x => x.Options)
            .WithOne(x => x.Question)
            .HasForeignKey(x => x.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.PollId);
    }
}

public sealed class PollOptionConfiguration : IEntityTypeConfiguration<PollOption>
{
    public void Configure(EntityTypeBuilder<PollOption> b)
    {
        b.ToTable("poll_options");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Text).IsRequired();
        b.HasIndex(x => x.QuestionId);
    }
}

public sealed class PollVoteConfiguration : IEntityTypeConfiguration<PollVote>
{
    public void Configure(EntityTypeBuilder<PollVote> b)
    {
        b.ToTable("poll_votes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Poll)
            .WithMany()
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // "One vote per user per poll" — the constraint the pollController
        // relies on for its E11000 → here 23505 → 409 DUPLICATE_ERROR mapping.
        b.HasIndex(x => new { x.PollId, x.UserId })
            .IsUnique()
            .HasDatabaseName("uq_poll_votes_one_per_user");

        b.HasIndex(x => x.SchoolId).HasDatabaseName("ix_poll_votes_school");

        b.HasMany(x => x.Answers)
            .WithOne(x => x.Vote)
            .HasForeignKey(x => x.VoteId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.SubmittedAt).HasDefaultValueSql("now()");
    }
}

public sealed class PollVoteAnswerConfiguration : IEntityTypeConfiguration<PollVoteAnswer>
{
    public void Configure(EntityTypeBuilder<PollVoteAnswer> b)
    {
        b.ToTable("poll_vote_answers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // Result tallies: SELECT option_id, COUNT(*) ... GROUP BY option_id.
        b.HasOne<PollQuestion>()
            .WithMany()
            .HasForeignKey(x => x.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<PollOption>()
            .WithMany()
            .HasForeignKey(x => x.OptionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One answer per question per vote — Node kept this by construction
        // (answers[i] ↔ questions[i]); a table needs it explicit.
        b.HasIndex(x => new { x.VoteId, x.QuestionId })
            .IsUnique()
            .HasDatabaseName("uq_poll_vote_answers_question");

        b.HasIndex(x => x.OptionId).HasDatabaseName("ix_poll_vote_answers_option");
    }
}

public sealed class RolePrivilegeConfiguration : IEntityTypeConfiguration<RolePrivilege>
{
    public void Configure(EntityTypeBuilder<RolePrivilege> b)
    {
        b.ToTable("role_privileges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Privilege).IsRequired();
        b.Property(x => x.Roles).HasColumnType("text[]");

        b.HasIndex(x => new { x.SchoolId, x.Privilege })
            .IsUnique()
            .HasDatabaseName("uq_role_privileges_school_priv");

        // roles @> ARRAY['teacher'] containment checks.
        b.HasIndex(x => x.Roles)
            .HasMethod("gin")
            .HasDatabaseName("ix_role_privileges_roles");

        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // NULLABLE — superadmin actions have no school.
        b.Property(x => x.SchoolId).IsRequired(false);

        b.Property(x => x.Action).IsRequired();

        // entityId TEXT — loose by design; ip TEXT — see entity comment.
        b.Property(x => x.Meta).HasColumnType("jsonb");

        // The list page's exact access path: per-school, newest first.
        b.HasIndex(x => new { x.SchoolId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_logs_school_time");

        b.HasIndex(x => x.Meta)
            .HasMethod("gin")
            .HasDatabaseName("ix_audit_logs_meta");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
    }
}
