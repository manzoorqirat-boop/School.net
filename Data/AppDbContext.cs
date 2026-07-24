using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data.Configurations;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure;
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
        // FIX (2nd pass): HasPostgresEnum<TEnum>() does NOT accept a labels
        // array — its real signature is
        //     HasPostgresEnum<TEnum>(schema, name, nameTranslator)
        // It derives labels itself by calling nameTranslator.TranslateMemberName
        // on each CLR member name. Passing a string[] as if it were a second
        // positional/labels argument doesn't compile against this overload
        // (that's the CS1729/CS0029 pile from the last attempt) — the compiler
        // was trying to fit the array into the `name`/`nameTranslator` slot.
        //
        // The previous plain `HasPostgresEnum("name", [...])` call (no <T>) DID
        // compile, but only registers the enum TYPE in Postgres — it never
        // links the CLR enum to it, so EF Core still defaulted every property
        // of that type to `integer`, which is the original bug.
        //
        // Fix: use the generic overload with the SAME EnumMemberNameTranslator
        // already defined in Infrastructure/EnumMemberNameTranslator.cs and
        // already used for the client-side NpgsqlDataSourceBuilder.MapEnum<T>()
        // calls in Program.cs. It reads each member's [EnumMember(Value=...)]
        // attribute from Enums.cs — the single source of truth — so labels
        // never need to be hand-typed (or drift) here at all.
        b.HasPostgresEnum<UserRole>(name: "user_role", nameTranslator: EnumMemberNameTranslator.For<UserRole>());
        b.HasPostgresEnum<SchoolType>(name: "school_type", nameTranslator: EnumMemberNameTranslator.For<SchoolType>());
        b.HasPostgresEnum<SchoolPlan>(name: "school_plan", nameTranslator: EnumMemberNameTranslator.For<SchoolPlan>());
        b.HasPostgresEnum<StudentStatus>(name: "student_status", nameTranslator: EnumMemberNameTranslator.For<StudentStatus>());
        b.HasPostgresEnum<Gender>(name: "gender", nameTranslator: EnumMemberNameTranslator.For<Gender>());
        b.HasPostgresEnum<StudentCategory>(name: "student_category", nameTranslator: EnumMemberNameTranslator.For<StudentCategory>());
        b.HasPostgresEnum<Religion>(name: "religion", nameTranslator: EnumMemberNameTranslator.For<Religion>());
        b.HasPostgresEnum<TransportMode>(name: "transport_mode", nameTranslator: EnumMemberNameTranslator.For<TransportMode>());
        b.HasPostgresEnum<SiblingRelation>(name: "sibling_relation", nameTranslator: EnumMemberNameTranslator.For<SiblingRelation>());
        b.HasPostgresEnum<AttendanceStatus>(name: "attendance_status", nameTranslator: EnumMemberNameTranslator.For<AttendanceStatus>());
        b.HasPostgresEnum<AttendanceMode>(name: "attendance_mode", nameTranslator: EnumMemberNameTranslator.For<AttendanceMode>());
        b.HasPostgresEnum<TeacherAttendanceStatus>(name: "teacher_attendance_status", nameTranslator: EnumMemberNameTranslator.For<TeacherAttendanceStatus>());
        b.HasPostgresEnum<ExamType>(name: "exam_type", nameTranslator: EnumMemberNameTranslator.For<ExamType>());
        b.HasPostgresEnum<ExamStatus>(name: "exam_status", nameTranslator: EnumMemberNameTranslator.For<ExamStatus>());
        b.HasPostgresEnum<ExamResultStatus>(name: "exam_result_status", nameTranslator: EnumMemberNameTranslator.For<ExamResultStatus>());
        b.HasPostgresEnum<GradingScaleType>(name: "grading_scale_type", nameTranslator: EnumMemberNameTranslator.For<GradingScaleType>());
        b.HasPostgresEnum<FeeFrequency>(name: "fee_frequency", nameTranslator: EnumMemberNameTranslator.For<FeeFrequency>());
        b.HasPostgresEnum<InvoiceStatus>(name: "invoice_status", nameTranslator: EnumMemberNameTranslator.For<InvoiceStatus>());
        b.HasPostgresEnum<PaymentMethod>(name: "payment_method", nameTranslator: EnumMemberNameTranslator.For<PaymentMethod>());
        b.HasPostgresEnum<PaymentStatus>(name: "payment_status", nameTranslator: EnumMemberNameTranslator.For<PaymentStatus>());
        b.HasPostgresEnum<TimetableStatus>(name: "timetable_status", nameTranslator: EnumMemberNameTranslator.For<TimetableStatus>());
        b.HasPostgresEnum<VariationType>(name: "variation_type", nameTranslator: EnumMemberNameTranslator.For<VariationType>());
        b.HasPostgresEnum<PayrollStatus>(name: "payroll_status", nameTranslator: EnumMemberNameTranslator.For<PayrollStatus>());
        b.HasPostgresEnum<PayrollRunStatus>(name: "payroll_run_status", nameTranslator: EnumMemberNameTranslator.For<PayrollRunStatus>());
        b.HasPostgresEnum<LeaveStatus>(name: "leave_status", nameTranslator: EnumMemberNameTranslator.For<LeaveStatus>());
        b.HasPostgresEnum<PollStatus>(name: "poll_status", nameTranslator: EnumMemberNameTranslator.For<PollStatus>());
        b.HasPostgresEnum<PollCategory>(name: "poll_category", nameTranslator: EnumMemberNameTranslator.For<PollCategory>());

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
