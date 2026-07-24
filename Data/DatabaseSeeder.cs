using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Data;

/// <summary>
/// Port of scripts/seed.js, plus the Seed__* / Rescue__* mechanisms from Parakh.
///
/// Idempotent throughout — every step is find-or-create, so it is safe to run on
/// every boot. That is what makes it usable as a Railway release command.
///
/// Runs with an UNFILTERED tenant context: the seeder has no JWT, so the global
/// query filter would otherwise hide every row it is trying to find, and it
/// would recreate the demo school on every boot.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<DatabaseSeeder> _log;

    public DatabaseSeeder(AppDbContext db, IConfiguration cfg, ILogger<DatabaseSeeder> log)
    {
        _db = db;
        _cfg = cfg;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await SeedSuperAdminAsync(ct);
        await RescueAsync(ct);

        if (_cfg.GetValue<bool>("SEED_DEMO_DATA"))
            await SeedDemoAsync(ct);
    }

    // ── Superadmin provisioning ───────────────────────────────────────────

    /// <summary>
    /// Seed__* : provision the platform superadmin from env.
    ///
    /// Note: .NET's environment-variable config provider maps "Seed__X" (double
    /// underscore) env vars into the IConfiguration key "Seed:X" (colon). Lookups
    /// here must use the colon form, or they will never match the env var.
    ///
    /// Deviation from seed.js: it defaults the password to 'Super@123'. That is
    /// fine for a local script but this runs on Railway boot, where a default
    /// password on a superadmin account is a publicly-known credential with
    /// cross-tenant read on every school. Production REQUIRES the env var.
    /// </summary>
    private async Task SeedSuperAdminAsync(CancellationToken ct)
    {
        var username = _cfg["Seed:SuperAdminUsername"]
                       ?? _cfg["SEED_SUPERADMIN_USERNAME"]
                       ?? "superadmin";

        var password = _cfg["Seed:SuperAdminPassword"]
                       ?? _cfg["SEED_SUPERADMIN_PASSWORD"];

        var existing = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username == username && u.SchoolId == null, ct);

        if (existing is not null)
        {
            _log.LogInformation("[seed] Superadmin '{Username}' exists, skipping", username);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            _log.LogError(
                "[seed] No superadmin exists and Seed__SuperAdminPassword is not set. " +
                "Set it and redeploy. Refusing to create a default-password superadmin.");
            return;
        }

        _db.Users.Add(new User
        {
            Username = username,
            Password = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10),
            Name = "Platform Superadmin",
            Role = UserRole.SuperAdmin,
            SchoolId = null,
            IsActive = true,
        });

        await _db.SaveChangesAsync(ct);
        _log.LogWarning("[seed] Superadmin created: {Username}. Rotate this password now.", username);
    }

    // ── Break-glass ───────────────────────────────────────────────────────

    /// <summary>
    /// Rescue__* : reset a locked-out account's password, or reactivate it.
    ///
    /// Same pattern as Parakh. Set the env vars, redeploy, then UNSET them —
    /// leaving them set means every boot resets that password, and anyone with
    /// Railway dashboard access holds a permanent backdoor.
    /// </summary>
    private async Task RescueAsync(CancellationToken ct)
    {
        var username = _cfg["Rescue:Username"];
        var password = _cfg["Rescue:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return;

        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
        {
            _log.LogError("[rescue] User '{Username}' not found", username);
            return;
        }

        user.Password = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.IsActive = true;

        // A password reset MUST kill every issued refresh token — otherwise
        // whoever prompted the rescue keeps minting access tokens.
        user.RevokeAllRefreshTokens();

        await _db.SaveChangesAsync(ct);

        _log.LogWarning(
            "[rescue] Password reset for '{Username}' and all sessions revoked. " +
            "UNSET Rescue__Username / Rescue__Password now.",
            username);
    }

    // ── Demo data ─────────────────────────────────────────────────────────

    private async Task SeedDemoAsync(CancellationToken ct)
    {
        var school = await _db.Schools
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == "demo-school", ct);

        if (school is null)
        {
            school = new School
            {
                Name = "Demo Public School",
                Slug = "demo-school",
                Type = SchoolType.K12,
                Email = "admin@demoschool.in",
                Phone = "+91-9999999999",
                City = "Delhi",
                State = "Delhi",
                Pincode = "110001",
                AcademicYear = "2025-2026",
                Classes = ["Nursery", "LKG", "UKG", "1", "2", "3", "4", "5",
                           "6", "7", "8", "9", "10", "11", "12"],
                Sections = ["A", "B", "C"],
                WorkingDays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
                Plan = SchoolPlan.Trial,
            };

            _db.Schools.Add(school);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("[seed] Demo school created: {Slug}", school.Slug);
        }

        // Demo credentials, unchanged from seed.js. Gated behind SEED_DEMO_DATA
        // precisely because these are published passwords.
        var demoUsers = new (string Username, string Password, string Name, UserRole Role)[]
        {
            ("admin",      "Admin@123",     "School Admin",     UserRole.SchoolAdmin),
            ("principal",  "Principal@123", "Dr. Priya Sharma", UserRole.Principal),
            ("accountant", "Accounts@123",  "Rakesh Kumar",     UserRole.Accountant),
            ("teacher1",   "Teacher@123",   "Anita Verma",      UserRole.Teacher),
            ("teacher2",   "Teacher@123",   "Suresh Iyer",      UserRole.Teacher),
            ("parent1",    "Parent@123",    "Ramesh Gupta",     UserRole.Parent),
        };

        foreach (var (username, password, name, role) in demoUsers)
        {
            var exists = await _db.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Username == username && u.SchoolId == school.Id, ct);

            if (exists) continue;

            _db.Users.Add(new User
            {
                SchoolId = school.Id,
                SchoolSlug = school.Slug,
                Username = username,
                Password = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10),
                Name = name,
                Role = role,
                IsActive = true,
            });

            _log.LogInformation("[seed] User created: {Username} ({Role})", username, role);
        }

        await _db.SaveChangesAsync(ct);

        await SeedDemoStudentsAsync(school, ct);
    }

    private async Task SeedDemoStudentsAsync(School school, CancellationToken ct)
    {
        var samples = new (string Adm, string Roll, string First, string Last,
                           string Class, string Section, Gender G, DateOnly Dob,
                           string Father, string Phone)[]
        {
            ("ADM001", "1", "Aarav",  "Gupta",  "5", "A", Gender.Male,
             new DateOnly(2014, 3, 10), "Ramesh Gupta", "+91-9000000001"),
            ("ADM002", "2", "Saanvi", "Sharma", "5", "A", Gender.Female,
             new DateOnly(2014, 7, 22), "Vikram Sharma", "+91-9000000002"),
        };

        foreach (var s in samples)
        {
            var exists = await _db.Students
                .IgnoreQueryFilters()
                .AnyAsync(x => x.AdmissionNo == s.Adm && x.SchoolId == school.Id, ct);

            if (exists) continue;

            _db.Students.Add(new Student
            {
                SchoolId = school.Id,
                AdmissionNo = s.Adm,
                RollNo = s.Roll,
                FirstName = s.First,
                LastName = s.Last,
                Class = s.Class,
                Section = s.Section,
                AcademicYear = school.AcademicYear,
                AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Gender = s.G,
                Dob = s.Dob,
                FatherName = s.Father,
                FatherPhone = s.Phone,
                Status = StudentStatus.Active,
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}

public static class SeederExtensions
{
    /// <summary>
    /// Call after Build(), before Run(). Creates its own scope with an unfiltered
    /// tenant so the seeder's find-or-create lookups can see existing rows.
    /// </summary>
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = new AppDbContext(
            sp.GetRequiredService<DbContextOptions<AppDbContext>>(),
            FixedTenantContext.Unfiltered(),
            sp.GetRequiredService<Infrastructure.Crypto.ICryptoService>());

        var seeder = new DatabaseSeeder(
            db,
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<DatabaseSeeder>>());

        await seeder.RunAsync();
    }
}
