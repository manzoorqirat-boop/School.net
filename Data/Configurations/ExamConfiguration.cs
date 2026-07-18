using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> b)
    {
        b.ToTable("exams");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Type).HasColumnName("type");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.Class).IsRequired();

        b.Property(x => x.FromDate).HasColumnType("date").IsRequired();
        b.Property(x => x.ToDate).HasColumnType("date").IsRequired();
        b.ToTable(t => t.HasCheckConstraint("ck_exams_dates", "to_date >= from_date"));

        // weightInFinal: 0–100 percent — numeric(5,2), NOT money.
        b.Property(x => x.WeightInFinal)
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(0m);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_exams_weight", "weight_in_final BETWEEN 0 AND 100"));

        b.HasOne(x => x.GradingScale)
            .WithMany()
            .HasForeignKey(x => x.GradingScaleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mongo's lookup index, verbatim. Non-unique, so nullable `section`
        // needs no NULLS NOT DISTINCT treatment here.
        b.HasIndex(x => new { x.SchoolId, x.AcademicYear, x.Class, x.Section, x.Type })
            .HasDatabaseName("ix_exams_lookup");

        b.HasMany(x => x.Subjects)
            .WithOne(x => x.Exam)
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class ExamSubjectConfiguration : IEntityTypeConfiguration<ExamSubject>
{
    public void Configure(EntityTypeBuilder<ExamSubject> b)
    {
        b.ToTable("exam_subjects");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Subject)
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.SubjectName).IsRequired();

        b.Property(x => x.MaxMarks).HasColumnType("numeric(6,2)");
        b.Property(x => x.PassingMark).HasColumnType("numeric(6,2)");
        b.Property(x => x.TheoryMax).HasColumnType("numeric(6,2)");
        b.Property(x => x.PracticalMax).HasColumnType("numeric(6,2)");
        b.Property(x => x.ExamDate).HasColumnType("date");

        b.ToTable(t => t.HasCheckConstraint(
            "ck_exam_subjects_marks",
            "max_marks >= 0 AND " +
            "(theory_max IS NULL OR theory_max >= 0) AND " +
            "(practical_max IS NULL OR practical_max >= 0)"));

        // One row per subject per exam. Not in the Mongo model (subdocs can't),
        // but a duplicate subject inside an exam is always a bug — the marksheet
        // grid keys rows by subjectId and would render the pair twice.
        b.HasIndex(x => new { x.ExamId, x.SubjectId })
            .IsUnique()
            .HasDatabaseName("uq_exam_subjects_exam_subject");
    }
}

public sealed class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> b)
    {
        b.ToTable("exam_results");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Exam)
            .WithMany()
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Subject)
            .WithMany()
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.EnteredByUser)
            .WithMany()
            .HasForeignKey(x => x.EnteredByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // NULL = absent; 0 = scored zero. decimal? preserves the distinction.
        b.Property(x => x.MarksObtained).HasColumnType("numeric(6,2)");
        b.Property(x => x.MaxMarks).HasColumnType("numeric(6,2)");
        b.Property(x => x.TheoryMarks).HasColumnType("numeric(6,2)");
        b.Property(x => x.PracticalMarks).HasColumnType("numeric(6,2)");

        // ONE decimal — matches Math.round(x*1000)/10 and the report-card PDFs.
        b.Property(x => x.Percentage).HasColumnType("numeric(4,1)");
        b.Property(x => x.Gpa).HasColumnType("numeric(4,2)");

        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.IsLocked).HasDefaultValue(false);

        // The absent invariant from the pre('save') hook, made structural: an
        // absent row cannot carry marks even if a code path forgets Recompute().
        b.ToTable(t => t.HasCheckConstraint(
            "ck_exam_results_absent",
            "status <> 'absent' OR marks_obtained IS NULL"));

        b.ToTable(t => t.HasCheckConstraint(
            "ck_exam_results_ranges",
            "(marks_obtained IS NULL OR marks_obtained >= 0) AND max_marks >= 0 AND " +
            "(percentage IS NULL OR percentage BETWEEN 0 AND 100) AND " +
            "(gpa IS NULL OR gpa BETWEEN 0 AND 10)"));

        // One result per student per exam per subject — all four NOT NULL,
        // plain unique ports faithfully. Bulk marksheet upsert conflicts on this.
        b.HasIndex(x => new { x.SchoolId, x.ExamId, x.StudentId, x.SubjectId })
            .IsUnique()
            .HasDatabaseName("uq_exam_results_key");

        // Report-card history per student.
        b.HasIndex(x => new { x.SchoolId, x.StudentId, x.AcademicYear })
            .HasDatabaseName("ix_exam_results_student_year");

        // Marksheet grid + class results.
        b.HasIndex(x => new { x.SchoolId, x.ExamId, x.Class, x.Section })
            .HasDatabaseName("ix_exam_results_exam_class");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class GradingScaleConfiguration : IEntityTypeConfiguration<GradingScale>
{
    public void Configure(EntityTypeBuilder<GradingScale> b)
    {
        b.ToTable("grading_scales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Type).HasColumnName("type");

        b.Property(x => x.PassingMark)
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(33m);

        b.Property(x => x.IsDefault).HasDefaultValue(false);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.HasIndex(x => new { x.SchoolId, x.Name })
            .IsUnique()
            .HasDatabaseName("uq_grading_scales_school_name");

        // At most ONE default scale per school. Mongo had no such guard —
        // isDefault was a plain boolean and two defaults were possible, with
        // "which one wins" left to query order. A partial unique closes it.
        b.HasIndex(x => x.SchoolId)
            .IsUnique()
            .HasFilter("is_default = true")
            .HasDatabaseName("uq_grading_scales_one_default");

        b.HasMany(x => x.Bands)
            .WithOne(x => x.GradingScale)
            .HasForeignKey(x => x.GradingScaleId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class GradeBandConfiguration : IEntityTypeConfiguration<GradeBand>
{
    public void Configure(EntityTypeBuilder<GradeBand> b)
    {
        b.ToTable("grading_bands");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Grade).IsRequired();
        b.Property(x => x.MinPercent).HasColumnType("numeric(5,2)");
        b.Property(x => x.MaxPercent).HasColumnType("numeric(5,2)");
        b.Property(x => x.Gpa).HasColumnType("numeric(4,2)");
        b.Property(x => x.IsPassing).HasDefaultValue(true);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_grading_bands_range",
            "min_percent BETWEEN 0 AND 100 AND max_percent BETWEEN 0 AND 100 " +
            "AND max_percent >= min_percent AND (gpa IS NULL OR gpa BETWEEN 0 AND 10)"));

        b.HasIndex(x => x.GradingScaleId);
    }
}
