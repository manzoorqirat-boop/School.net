using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data.Configurations;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Crypto;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Data;

/// <summary>
/// The tenant filter lives here, once, for every tenant-scoped entity.
///
/// The Node original spread `{ schoolId: req.tenantId }` by hand across every
/// query — forget it once and School A reads School B. This makes that
/// structurally impossible: a handler cannot opt in to a leak, only explicitly
/// out (IgnoreQueryFilters), which greps cleanly in review.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;
    private readonly ICryptoService _crypto;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenant,
        ICryptoService crypto)
        : base(options)
    {
        _tenant = tenant;
        _crypto = crypto;
    }

    public DbSet<School> Schools => Set<School>();
    public DbSet<SchoolLeaveType> SchoolLeaveTypes => Set<SchoolLeaveType>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentPassedExam> StudentPassedExams => Set<StudentPassedExam>();
    public DbSet<StudentSibling> StudentSiblings => Set<StudentSibling>();
    public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<TeacherAttendance> TeacherAttendance => Set<TeacherAttendance>();
    public DbSet<ClassTeacher> ClassTeachers => Set<ClassTeacher>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamSubject> ExamSubjects => Set<ExamSubject>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<GradingScale> GradingScales => Set<GradingScale>();
    public DbSet<GradeBand> GradeBands => Set<GradeBand>();
    public DbSet<FeeStructure> FeeStructures => Set<FeeStructure>();
    public DbSet<FeeHead> FeeHeads => Set<FeeHead>();
    public DbSet<FeeInstallment> FeeInstallments => Set<FeeInstallment>();
    public DbSet<FeeInvoice> FeeInvoices => Set<FeeInvoice>();
    public DbSet<FeeInvoiceLine> FeeInvoiceLines => Set<FeeInvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<Timetable> Timetables => Set<Timetable>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<TimetableVariation> TimetableVariations => Set<TimetableVariation>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
    public DbSet<Payroll> Payrolls => Set<Payroll>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Leave> Leaves => Set<Leave>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollQuestion> PollQuestions => Set<PollQuestion>();
    public DbSet<PollOption> PollOptions => Set<PollOption>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();
    public DbSet<PollVoteAnswer> PollVoteAnswers => Set<PollVoteAnswer>();
    public DbSet<RolePrivilege> RolePrivileges => Set<RolePrivilege>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.HasPostgresExtension("citext");    // case-insensitive username/email/slug
        b.HasPostgresExtension("pg_trgm");   // ILIKE student search

        // Native Postgres enums.
        //
        // ⚠️ Labels are passed EXPLICITLY via the NON-GENERIC overload. The
        // generic HasPostgresEnum<TEnum>(schema, name) overload does NOT accept
        // a labels array in this provider version — its second parameter is
        // `name`, not `labels` — so passing an array there is a compile error,
        // not a silent behavior change. This non-generic form just declares the
        // Postgres enum TYPE (with our chosen labels) so migrations can
        // CREATE TYPE it correctly.
        //
        // ⚠️ This alone does NOT link the type to a CLR enum for column
        // generation — that link is made separately, via
        // `npg.MapEnum<TEnum>(pgName, translator)` inside the
        // `opt.UseNpgsql(dataSource, npg => ...)` callback in Program.cs. That
        // is what makes EF generate `mode <enum_type>` columns instead of
        // defaulting to `integer`, and what makes raw-SQL check constraints
        // written against text labels (e.g. "mode = 'period'") actually match
        // the stored representation.
        b.HasPostgresEnum("user_role",
            ["superadmin", "school_admin", "principal", "accountant",
             "teacher", "parent", "student"]);
        b.HasPostgresEnum("school_type", ["k12", "coaching", "college", "other"]);
        b.HasPostgresEnum("school_plan", ["trial", "basic", "pro", "enterprise"]);
        b.HasPostgresEnum("student_status",
            ["active", "inactive", "transferred", "graduated"]);
        b.HasPostgresEnum("gender", ["male", "female", "other"]);
        b.HasPostgresEnum("student_category", ["GEN", "OBC", "SC", "ST", "EWS"]);
        b.HasPostgresEnum("religion",
            ["Hindu", "Muslim", "Sikh", "Christian", "Buddhist", "Jain", "Other"]);
        b.HasPostgresEnum("transport_mode", ["self", "school_bus", "walk", "other"]);
        b.HasPostgresEnum("sibling_relation", ["brother", "sister"]);
        b.HasPostgresEnum("attendance_status",
            ["present", "absent", "late", "leave", "holiday"]);
        b.HasPostgresEnum("attendance_mode", ["daily", "period"]);
        b.HasPostgresEnum("teacher_attendance_status",
            ["present", "absent", "half_day", "leave", "unpaid_leave",
             "on_duty", "holiday"]);
        b.HasPostgresEnum("exam_type",
            ["unit_test", "periodic", "term", "half_yearly", "annual", "custom"]);
        b.HasPostgresEnum("exam_status",
            ["draft", "scheduled", "in_progress", "completed", "published"]);
        b.HasPostgresEnum("exam_result_status", ["absent", "present", "exempt"]);
        b.HasPostgresEnum("grading_scale_type", ["marks", "grade", "gpa", "pass_fail"]);
        b.HasPostgresEnum("fee_frequency",
            ["one_time", "monthly", "quarterly", "half_yearly", "annual"]);
        b.HasPostgresEnum("invoice_status",
            ["pending", "partial", "paid", "overdue", "cancelled"]);
        b.HasPostgresEnum("payment_method",
            ["cash", "cheque", "upi", "card", "bank_transfer", "razorpay"]);
        b.HasPostgresEnum("payment_status",
            ["pending", "success", "failed", "refunded"]);
        b.HasPostgresEnum("timetable_status", ["draft", "active", "archived"]);
        b.HasPostgresEnum("variation_type",
            ["substitute_teacher", "cancelled", "rescheduled", "guest_lecture", "custom"]);
        b.HasPostgresEnum("payroll_status",
            ["draft", "generated", "locked", "paid", "failed"]);
        b.HasPostgresEnum("payroll_run_status",
            ["draft", "generated", "locked", "transfer_queued",
             "transfer_completed", "cancelled"]);
        b.HasPostgresEnum("leave_status", ["pending", "approved", "rejected"]);
        b.HasPostgresEnum("poll_status", ["draft", "active", "closed"]);
        b.HasPostgresEnum("poll_category",
            ["satisfaction", "event", "canteen", "general"]);

        // SchoolConfiguration needs ICryptoService, so it cannot be discovered by
        // ApplyConfigurationsFromAssembly (which requires a parameterless ctor).
        // Applied explicitly; the rest are scanned.
        b.ApplyConfiguration(new SchoolConfiguration(_crypto));
        b.ApplyConfiguration(new PaymentConfiguration(_crypto));
        b.ApplyConfiguration(new SalaryStructureConfiguration(_crypto));
        b.ApplyConfiguration(new PayrollConfiguration(_crypto));
        b.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            t => t != typeof(SchoolConfiguration)
              && t != typeof(PaymentConfiguration)
              && t != typeof(SalaryStructureConfiguration)
              && t != typeof(PayrollConfiguration));

        ApplyGlobalFilters(b);
    }

    /// <summary>
    /// EF Core allows exactly ONE query filter per entity type — a second
    /// HasQueryFilter() silently REPLACES the first. Tenant and soft-delete must
    /// therefore compose into a single expression per entity.
    /// </summary>
    private void ApplyGlobalFilters(ModelBuilder b)
    {
        foreach (var et in b.Model.GetEntityTypes())
        {
            var clr = et.ClrType;

            // School is the tenant root — a filter on it would be self-referential
            // and would hide every school from the superadmin school list.
            if (clr == typeof(School)) continue;

            // User and AuditLog carry NULLABLE SchoolIds (superadmin has no
            // school; superadmin actions audit school-less), so neither can
            // implement ITenantScoped. Both filtered explicitly below.
            if (clr == typeof(User)) continue;
            if (clr == typeof(AuditLog)) continue;

            var tenantScoped = typeof(ITenantScoped).IsAssignableFrom(clr);
            var softDelete = typeof(ISoftDeletable).IsAssignableFrom(clr);
            if (!tenantScoped && !softDelete) continue;

            var p = Expression.Parameter(clr, "e");
            Expression? body = null;

            if (tenantScoped)
                body = TenantPredicate(Expression.Property(p, nameof(ITenantScoped.SchoolId)));

            if (softDelete)
            {
                var notDeleted = Expression.Not(
                    Expression.Property(p, nameof(ISoftDeletable.IsDeleted)));
                body = body is null ? notDeleted : Expression.AndAlso(body, notDeleted);
            }

            b.Entity(clr).HasQueryFilter(Expression.Lambda(body!, p));
        }

        // ── User: nullable tenant ─────────────────────────────────────────
        // A superadmin row has school_id = NULL. Under this predicate,
        // `NULL == <someGuid>` is false, so a school_admin cannot see
        // superadmins — correct and intended.
        b.Entity<User>().HasQueryFilter(u =>
            !CurrentTenant.IsFilterActive || u.SchoolId == CurrentTenant.SchoolId);

        // AuditLog: same nullable-tenant shape. A school_admin sees only their
        // school's rows; superadmin (filter inactive) sees everything including
        // school-less superadmin actions.
        b.Entity<AuditLog>().HasQueryFilter(a =>
            !CurrentTenant.IsFilterActive || a.SchoolId == CurrentTenant.SchoolId);
    }

    /// <summary>
    /// Builds: !CurrentTenant.IsFilterActive || e.SchoolId == CurrentTenant.SchoolId
    ///
    /// Reads the tenant through a PROPERTY on `this`, never a captured constant.
    /// EF compiles and caches the filter once per model; baking in request #1's
    /// Guid would serve School A's rows to every later request — the exact leak
    /// this class exists to prevent.
    /// </summary>
    private Expression TenantPredicate(Expression schoolIdProp)
    {
        var self = Expression.Constant(this);
        var tenant = Expression.Property(self, nameof(CurrentTenant));

        var currentSchool = Expression.Property(tenant, nameof(ITenantContext.SchoolId));
        var filterActive = Expression.Property(tenant, nameof(ITenantContext.IsFilterActive));

        var matches = Expression.Equal(
            Expression.Convert(schoolIdProp, typeof(Guid?)),
            currentSchool);

        return Expression.OrElse(Expression.Not(filterActive), matches);
    }

    /// <summary>Public so the compiled filter expressions can reach it.</summary>
    public ITenantContext CurrentTenant => _tenant;

    public override int SaveChanges()
    {
        Stamp();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Stamp();
        return base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Replaces 20+ identical `pre('save')` hooks setting updatedAt — which did
    /// not fire on updateOne(), so updatedAt is already unreliable in the Mongo
    /// app. This one cannot be missed.
    /// </summary>
    private void Stamp()
    {
        var now = DateTime.UtcNow;

        foreach (var e in ChangeTracker.Entries())
        {
            if (e.Entity is IAuditable a)
            {
                if (e.State == EntityState.Added) a.CreatedAt = now;
                if (e.State is EntityState.Added or EntityState.Modified) a.UpdatedAt = now;
            }

            // Stamp the tenant on insert so a handler cannot create a row into the
            // wrong school (or none) and have the filter hide it forever.
            if (e.State == EntityState.Added
                && e.Entity is ITenantScoped t
                && t.SchoolId == Guid.Empty
                && _tenant.SchoolId.HasValue)
            {
                t.SchoolId = _tenant.SchoolId.Value;
            }

            // Intercept Remove() → soft delete. Handlers should set IsDeleted
            // directly; this is belt-and-braces so a stray .Remove() cannot
            // hard-delete a student.
            if (e.State == EntityState.Deleted && e.Entity is ISoftDeletable s)
            {
                e.State = EntityState.Modified;
                s.IsDeleted = true;
                s.DeletedAt = now;
            }
        }
    }
}
