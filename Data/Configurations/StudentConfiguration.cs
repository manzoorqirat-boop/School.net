using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.ToTable("students");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.School)
            .WithMany(x => x.Students)
            .HasForeignKey(x => x.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.AdmissionNo).IsRequired();
        b.Property(x => x.FirstName).IsRequired();
        b.Property(x => x.Class).IsRequired();
        b.Property(x => x.Section).IsRequired();
        b.Property(x => x.AcademicYear).IsRequired();

        b.Property(x => x.Email).HasColumnType("citext");
        b.Property(x => x.Nationality).HasDefaultValue("Indian");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.ShareEnabled).HasDefaultValue(false);
        b.Property(x => x.IsDeleted).HasDefaultValue(false);

        // date, not timestamptz — IST is UTC+5:30, so a "date" written at
        // 23:00 IST lands on the PREVIOUS UTC day. This also deletes Mongo's
        // setUTCHours(0,0,0,0) normalisation hack.
        b.Property(x => x.Dob).HasColumnType("date");
        b.Property(x => x.AdmissionDate).HasColumnType("date").HasDefaultValueSql("CURRENT_DATE");
        b.Property(x => x.TcDate).HasColumnType("date");

        // ── Unique admission number ───────────────────────────────────────
        // Mongo: index({ schoolId, admissionNo }, { unique: true }) — no
        // isDeleted in the key, so a soft-deleted student BLOCKS reuse of their
        // admission number forever. Preserved deliberately: admission numbers are
        // a permanent register in Indian schools and must not be recycled.
        // Making this partial on `is_deleted = false` would be a behaviour change.
        b.HasIndex(x => new { x.SchoolId, x.AdmissionNo })
            .IsUnique()
            .HasDatabaseName("uq_students_school_admission");

        b.HasIndex(x => new { x.SchoolId, x.Class, x.Section })
            .HasDatabaseName("ix_students_school_class_section");

        // ── Share token ───────────────────────────────────────────────────
        // Mongo: { index: true, sparse: true }. A plain index would work, but
        // partial keeps it small — only a handful of students ever share.
        //
        // Looked up UNAUTHENTICATED on the hot path
        // (GET /api/students/public/:token), so it must be indexed.
        b.HasIndex(x => x.ShareToken)
            .HasFilter("share_token IS NOT NULL")
            .HasDatabaseName("ix_students_share_token");

        // ── Search (GET /api/students?q=) ─────────────────────────────────
        // Mongo ran a case-insensitive regex across 9 fields. Postgres: ILIKE
        // over pg_trgm GIN indexes.
        //
        // Only the fields people actually search by name are indexed; phone
        // columns are left to a sequential scan within the already-filtered
        // tenant slice. Nine GIN indexes would cost more on every INSERT than
        // they save on an occasional search.
        b.HasIndex(x => x.FirstName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_students_firstname_trgm");

        b.HasIndex(x => x.LastName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_students_lastname_trgm");

        b.HasIndex(x => x.AdmissionNo)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_students_admno_trgm");

        // Soft-delete: keeps the filtered index small since most rows are live.
        b.HasIndex(x => x.IsDeleted)
            .HasFilter("is_deleted = true")
            .HasDatabaseName("ix_students_deleted");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        b.HasMany(x => x.PassedExams)
            .WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Siblings)
            .WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.DisplayName);
    }
}

public sealed class StudentPassedExamConfiguration : IEntityTypeConfiguration<StudentPassedExam>
{
    public void Configure(EntityTypeBuilder<StudentPassedExam> b)
    {
        b.ToTable("student_passed_exams");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        // Marks, not money — but numeric all the same: a double makes
        // 33.33 x 3 fail to sum to 100.
        b.Property(x => x.MaxMarks).HasColumnType("numeric(6,2)");
        b.Property(x => x.ObtainedMarks).HasColumnType("numeric(6,2)");

        b.ToTable(t => t.HasCheckConstraint(
            "ck_passed_exams_marks",
            "(max_marks IS NULL OR max_marks >= 0) AND " +
            "(obtained_marks IS NULL OR obtained_marks >= 0)"));

        b.HasIndex(x => x.StudentId);
    }
}

public sealed class StudentSiblingConfiguration : IEntityTypeConfiguration<StudentSibling>
{
    public void Configure(EntityTypeBuilder<StudentSibling> b)
    {
        b.ToTable("student_siblings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.SameSchool).HasDefaultValue(true);
        b.Property(x => x.Relation).HasColumnName("relation");

        b.HasIndex(x => x.StudentId);
    }
}
