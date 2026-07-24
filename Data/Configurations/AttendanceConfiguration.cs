using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Data.Configurations;

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> b)
    {
        b.ToTable("attendance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.MarkedByUser)
            .WithMany()
            .HasForeignKey(x => x.MarkedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.Class).IsRequired();
        b.Property(x => x.Section).IsRequired();
        b.Property(x => x.Date).HasColumnType("date").IsRequired();

        // Mode maps to the native Postgres enum "attendance_mode" — registered
        // via HasPostgresEnum<AttendanceMode>(...) in AppDbContext.OnModelCreating
        // and NpgsqlDataSourceBuilder.MapEnum<AttendanceMode>(...) in Program.cs.
        // No per-property conversion needed here; EF Core picks up the native
        // enum column type automatically from the CLR type registration.
        b.Property(x => x.Mode).HasColumnName("mode");

        // NOTE: If Status is also an enum and is ever referenced as a string
        // literal (check constraint, index filter, seed data), apply the same
        // .HasConversion<string>() here. Leaving as default (int) for now since
        // no string comparison was found against it in this file.
        b.Property(x => x.Status).HasColumnName("status");

        b.Property(x => x.Period).HasColumnType("smallint");

        // period ⇔ mode='period'. The Mongo model only had min/max; the mode
        // linkage was enforced by controller convention. Making it structural
        // costs nothing and kills a whole class of bad rows.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_attendance_period_mode",
            "(mode = 'period') = (period IS NOT NULL) AND " +
            "(period IS NULL OR period BETWEEN 1 AND 12)"));

        // ── THE NULL TRAP (SCHEMA-MAP §2.1) ───────────────────────────────
        // Mongo: ONE unique index over {schoolId, studentId, date, mode, period,
        // subject}. In Mongo, nulls COLLIDE — two daily rows with period:null
        // conflict, which is the intent: one daily record per student per day.
        //
        // In Postgres NULL != NULL, so the same index as a plain UNIQUE lets
        // duplicate daily attendance through SILENTLY. Every daily row has
        // period=NULL and subject=NULL, so the constraint would never fire.
        //
        // Two partial indexes express the actual rule:
        b.HasIndex(x => new { x.SchoolId, x.StudentId, x.Date })
            .IsUnique()
            .HasFilter("mode = 'daily'")
            .HasDatabaseName("uq_attendance_daily");

        // Period rows: subject can still be NULL (the Mongo field is optional),
        // and NULL != NULL means two period-5 rows with subject NULL would both
        // insert. AreNullsDistinct(false) emits NULLS NOT DISTINCT (Postgres 15+,
        // which Railway runs) — nulls then collide, matching Mongo's semantics
        // exactly. Verified against the Npgsql efcore.pg v8.0.10 source.
        b.HasIndex(x => new { x.SchoolId, x.StudentId, x.Date, x.Period, x.Subject })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("mode = 'period'")
            .HasDatabaseName("uq_attendance_period");

        // Roster lookup: GET /api/attendance/roster
        b.HasIndex(x => new { x.SchoolId, x.Class, x.Section, x.Date, x.Mode })
            .HasDatabaseName("ix_attendance_roster");

        // Per-student history: GET /api/attendance/student/:id
        b.HasIndex(x => new { x.SchoolId, x.StudentId, x.Date })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_attendance_student_history");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class TeacherAttendanceConfiguration : IEntityTypeConfiguration<TeacherAttendance>
{
    public void Configure(EntityTypeBuilder<TeacherAttendance> b)
    {
        b.ToTable("teacher_attendance");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.MarkedByUser)
            .WithMany()
            .HasForeignKey(x => x.MarkedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.Date).HasColumnType("date").IsRequired();
        b.Property(x => x.Status).HasColumnName("status");

        // One record per teacher per day. All three columns NOT NULL — no NULL
        // trap here, a plain unique ports faithfully.
        b.HasIndex(x => new { x.SchoolId, x.TeacherId, x.Date })
            .IsUnique()
            .HasDatabaseName("uq_teacher_attendance_day");

        // Per-day roster across the school.
        b.HasIndex(x => new { x.SchoolId, x.Date })
            .HasDatabaseName("ix_teacher_attendance_roster");

        // Per-teacher history, newest first — feeds the monthly payroll sum.
        b.HasIndex(x => new { x.SchoolId, x.TeacherId, x.Date })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_teacher_attendance_history");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class ClassTeacherConfiguration : IEntityTypeConfiguration<ClassTeacher>
{
    public void Configure(EntityTypeBuilder<ClassTeacher> b)
    {
        b.ToTable("class_teachers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.HasOne(x => x.TeacherUser)
            .WithMany()
            .HasForeignKey(x => x.TeacherUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(x => x.AcademicYear).IsRequired();
        b.Property(x => x.Class).IsRequired();
        b.Property(x => x.Section).IsRequired();
        b.Property(x => x.IsPrimary).HasDefaultValue(true);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        // ── THE SAME NULL TRAP, UNFLAGGED IN SCHEMA-MAP ───────────────────
        // Mongo unique: {schoolId, academicYear, class, section, subject,
        // teacherUserId} — and `subject` is OPTIONAL. In Mongo, two homeroom
        // rows (subject:null) for the same teacher+class+section COLLIDE, which
        // is the intent: one general assignment per teacher per class.
        //
        // A plain Postgres UNIQUE would let unlimited duplicate homeroom rows
        // through (every one has subject=NULL, and NULL != NULL).
        // AreNullsDistinct(false) → NULLS NOT DISTINCT: nulls collide, matching
        // the Mongo single-index semantics exactly.
        b.HasIndex(x => new { x.SchoolId, x.AcademicYear, x.Class, x.Section, x.Subject, x.TeacherUserId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("uq_class_teachers_assignment");

        // GET /api/class-teachers/my-classes — a teacher's own assignments.
        b.HasIndex(x => new { x.SchoolId, x.TeacherUserId, x.AcademicYear })
            .HasDatabaseName("ix_class_teachers_by_teacher");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> b)
    {
        b.ToTable("subjects");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.Class).IsRequired();
        b.Property(x => x.AcademicYear).IsRequired();

        // Mongo `uppercase: true` — normalised on write. A ValueConverter beats
        // scattering ToUpperInvariant() across handlers (bulk import writes too).
        b.Property(x => x.Code)
            .HasConversion(
                v => v == null ? null : v.ToUpperInvariant(),
                v => v);

        // Marks-out-of, not money — but numeric all the same.
        b.Property(x => x.DefaultMaxMarks)
            .HasColumnType("numeric(6,2)")
            .HasDefaultValue(100m);

        b.Property(x => x.DisplayOrder).HasDefaultValue(0);
        b.Property(x => x.IsCoScholastic).HasDefaultValue(false);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        // All four NOT NULL — plain unique ports faithfully.
        b.HasIndex(x => new { x.SchoolId, x.AcademicYear, x.Class, x.Name })
            .IsUnique()
            .HasDatabaseName("uq_subjects_school_year_class_name");

        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}
