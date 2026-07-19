using Hangfire;
using Hangfire.PostgreSql;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Infrastructure;
using QMSoft.Api.Infrastructure.Auth;
using QMSoft.Api.Infrastructure.Crypto;
using QMSoft.Api.Infrastructure.Tenancy;
using QMSoft.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ─── Boot-time guards ─────────────────────────────────────────────────────────
// Port of assertEncryptionKeyAtBoot(). Must run BEFORE the host starts:
// a missing key in production would silently store bank/cheque/Razorpay data
// as plaintext with only a log line to show for it.
CryptoService.AssertKeyAtBoot(builder.Configuration, builder.Environment);

// NOTE: Node's cluster module is deliberately NOT ported. Kestrel is already
// multi-threaded, and Railway assigns one vCPU — forking here would just
// multiply memory for no throughput.

// ─── Database ─────────────────────────────────────────────────────────────────
var rawConn = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("No Postgres connection string configured.");

// Railway (and Heroku-style platforms) provide a URI:
//   postgresql://user:pass@host:5432/railway
// but NpgsqlDataSourceBuilder accepts only keyword format:
//   Host=...;Port=...;Username=...;Password=...;Database=...
// Convert when needed so either shape works. Query params (e.g. sslmode) are
// passed through. Npgsql's default SslMode=Prefer handles both Railway's
// internal (no TLS) and public-proxy (TLS) endpoints.
var connString = ToNpgsqlKeywordFormat(rawConn);

static string ToNpgsqlKeywordFormat(string conn)
{
    if (!conn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !conn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return conn;   // already keyword format
    }

    var uri = new Uri(conn);
    var userInfo = uri.UserInfo.Split(':', 2);

    var kv = new List<string>
    {
        $"Host={uri.Host}",
        $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
        $"Username={Uri.UnescapeDataString(userInfo[0])}",
        $"Database={uri.AbsolutePath.TrimStart('/')}",
    };

    if (userInfo.Length > 1)
        kv.Add($"Password={Uri.UnescapeDataString(userInfo[1])}");

    // Pass through ?sslmode=... and friends.
    var query = uri.Query.TrimStart('?');
    if (!string.IsNullOrEmpty(query))
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = parts[0].ToLowerInvariant() switch
            {
                "sslmode" => "SSL Mode",
                "connect_timeout" => "Timeout",
                _ => parts[0],
            };
            kv.Add($"{key}={(parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "")}");
        }
    }

    return string.Join(';', kv);
}

// Npgsql needs the CLR enum ↔ Postgres label mapping at the DATA SOURCE level.
//
// The default NpgsqlNameTranslator snake_cases member names — the same broken
// rule that yields 'super_admin' and 'g_e_n'. A custom translator reads
// [EnumMember] instead, so there is exactly one source of truth (Enums.cs) for
// the wire value, the DB label, and the JSON string.
var dsb = new NpgsqlDataSourceBuilder(connString);

// One translator per enum — a shared map would collide: Gender.Other and
// TransportMode.Other are "other" but Religion.Other is "Other".
// For<T>() throws at boot if any member lacks [EnumMember].
dsb.MapEnum<UserRole>("user_role", EnumMemberNameTranslator.For<UserRole>());
dsb.MapEnum<SchoolType>("school_type", EnumMemberNameTranslator.For<SchoolType>());
dsb.MapEnum<SchoolPlan>("school_plan", EnumMemberNameTranslator.For<SchoolPlan>());
dsb.MapEnum<StudentStatus>("student_status", EnumMemberNameTranslator.For<StudentStatus>());
dsb.MapEnum<Gender>("gender", EnumMemberNameTranslator.For<Gender>());
dsb.MapEnum<StudentCategory>("student_category", EnumMemberNameTranslator.For<StudentCategory>());
dsb.MapEnum<Religion>("religion", EnumMemberNameTranslator.For<Religion>());
dsb.MapEnum<TransportMode>("transport_mode", EnumMemberNameTranslator.For<TransportMode>());
dsb.MapEnum<SiblingRelation>("sibling_relation", EnumMemberNameTranslator.For<SiblingRelation>());
dsb.MapEnum<AttendanceStatus>("attendance_status", EnumMemberNameTranslator.For<AttendanceStatus>());
dsb.MapEnum<AttendanceMode>("attendance_mode", EnumMemberNameTranslator.For<AttendanceMode>());
dsb.MapEnum<TeacherAttendanceStatus>("teacher_attendance_status", EnumMemberNameTranslator.For<TeacherAttendanceStatus>());
dsb.MapEnum<ExamType>("exam_type", EnumMemberNameTranslator.For<ExamType>());
dsb.MapEnum<ExamStatus>("exam_status", EnumMemberNameTranslator.For<ExamStatus>());
dsb.MapEnum<ExamResultStatus>("exam_result_status", EnumMemberNameTranslator.For<ExamResultStatus>());
dsb.MapEnum<GradingScaleType>("grading_scale_type", EnumMemberNameTranslator.For<GradingScaleType>());
dsb.MapEnum<FeeFrequency>("fee_frequency", EnumMemberNameTranslator.For<FeeFrequency>());
dsb.MapEnum<InvoiceStatus>("invoice_status", EnumMemberNameTranslator.For<InvoiceStatus>());
dsb.MapEnum<PaymentMethod>("payment_method", EnumMemberNameTranslator.For<PaymentMethod>());
dsb.MapEnum<PaymentStatus>("payment_status", EnumMemberNameTranslator.For<PaymentStatus>());
dsb.MapEnum<TimetableStatus>("timetable_status", EnumMemberNameTranslator.For<TimetableStatus>());
dsb.MapEnum<VariationType>("variation_type", EnumMemberNameTranslator.For<VariationType>());
dsb.MapEnum<PayrollStatus>("payroll_status", EnumMemberNameTranslator.For<PayrollStatus>());
dsb.MapEnum<PayrollRunStatus>("payroll_run_status", EnumMemberNameTranslator.For<PayrollRunStatus>());
dsb.MapEnum<LeaveStatus>("leave_status", EnumMemberNameTranslator.For<LeaveStatus>());
dsb.MapEnum<PollStatus>("poll_status", EnumMemberNameTranslator.For<PollStatus>());
dsb.MapEnum<PollCategory>("poll_category", EnumMemberNameTranslator.For<PollCategory>());
var dataSource = dsb.Build();

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(dataSource, npg =>
    {
        npg.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
        npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
    });

    // Columns/keys in snake_case (school_id, amount_paid…). Explicit
    // ToTable()/HasColumnName() calls still win where present. All raw SQL in
    // the configurations depends on this — see csproj note.
    opt.UseSnakeCaseNamingConvention();

    if (builder.Environment.IsDevelopment())
    {
        opt.EnableSensitiveDataLogging();
        opt.EnableDetailedErrors();
    }

    // EF warns (10622) that child tables (FeeInvoiceLine, ExamSubject, …) lack
    // the tenant filter their parents carry. By design: children have no
    // school_id and are reachable only through their parent, whose filter
    // applies to any query that touches the navigation.
    //
    // THE RULE THIS ENCODES: never query a child DbSet directly without going
    // through its parent (e.g. db.FeeInvoiceLines.Where(l => l.Invoice…) is
    // fine — the Invoice filter kicks in; a bare db.FeeInvoiceLines scan is
    // not, and would cross tenants). Phase 3 handlers must follow it.
    opt.ConfigureWarnings(w => w.Ignore(
        Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId
            .PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
});

// ─── Tenancy ──────────────────────────────────────────────────────────────────
// Scoped: resolved per-request from JWT claims. No DB hit, unlike tenant.js.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// ─── Crypto / auth ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<ICryptoService, CryptoService>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<ITokenRevocationStore, TokenRevocationStore>();

// Redis kept ONLY for rate limiting + the token blacklist (SCHEMA-MAP §4.2).
// The user/school caches from middleware/auth.js + tenant.js are deliberately
// dropped: they solved a Mongoose problem (findById per request, no pooling)
// that Npgsql pooling doesn't have. Add IMemoryCache later if a profile says so —
// but then the invalidation must come with it, or a stale auth cache after a
// deactivation is a security hole.
var redis = builder.Configuration["REDIS_URL"];
if (!string.IsNullOrWhiteSpace(redis))
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redis;
        o.InstanceName = "qms:";
    });
}
else
{
    // Dev fallback. In production this means the token blacklist is per-instance —
    // acceptable on single-instance Railway, NOT on a scaled deployment.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var secret = builder.Configuration["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is not set.");

        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JWT_ISSUER"] ?? "qmsoft",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JWT_AUDIENCE"] ?? "qmsoft",
            ValidateLifetime = true,

            // ClockSkew MUST be zero. The default is FIVE MINUTES, which would
            // keep a 15-minute token alive for 20 and desync the frontend's
            // refresh timing from the server's idea of expiry.
            ClockSkew = TimeSpan.Zero,
        };

        // The whole TOKEN_EXPIRED / INVALID_TOKEN contract lives here.
        o.Events = JwtEvents.Create();
    });

// ─── Authorization ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PrivilegePolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PrivilegeHandler>();
builder.Services.AddScoped<IPrivilegeResolver, DbPrivilegeResolver>();
builder.Services.AddMemoryCache();                       // auth rate limiting
builder.Services.AddScoped<QMSoft.Api.Features.IAuditWriter, QMSoft.Api.Features.AuditWriter>();
builder.Services.AddScoped<QMSoft.Api.Features.Students.ParentLinkService>();
builder.Services.AddScoped<QMSoft.Api.Features.Payments.RazorpayService>();
builder.Services.AddScoped<QMSoft.Api.Features.Documents.PdfService>();
builder.Services.AddScoped<QMSoft.Api.Features.Jobs.LateFeeJob>();

// Hangfire — Postgres-backed recurring jobs (replaces BullMQ). Uses the same DB.
builder.Services.AddHangfire(cfg => cfg.UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(connString)));
builder.Services.AddHangfireServer();

// QuestPDF community licence — required, set once at startup.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddAuthorization();

// ─── MVC + JSON ───────────────────────────────────────────────────────────────
builder.Services
    .AddControllers(o =>
    {
        // Reject unknown query/body shapes loudly rather than binding defaults.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = false;
    })
    .AddJsonOptions(o =>
    {
        // camelCase everywhere EXCEPT _id, which is handled per-property by
        // [JsonPropertyName("_id")] on the entities. A naming policy cannot
        // express that — it is not a casing difference.
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;

        // Money: decimal → JSON number (1234.50, not "1234.50").
        // api.ts inr() does Number(n).toLocaleString() — a string would break
        // every rupee display in the app. This is the default; do NOT add a
        // string converter.
    });

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ─── CORS ─────────────────────────────────────────────────────────────────────
// Node had cors({ origin: true, credentials: true }) — reflects ANY origin.
// Reflecting any origin AND allowing credentials is a combination browsers
// reject outright, and it's not what we want anyway. The frontend sends a
// Bearer header from localStorage, not cookies — so credentials aren't needed.
var allowedOrigins = builder.Configuration["CORS_ORIGINS"]?.Split(',',
    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins is { Length: > 0 })
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    else if (builder.Environment.IsDevelopment())
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        throw new InvalidOperationException(
            "CORS_ORIGINS must be set in production.");
}));

builder.Services.AddResponseCompression();

// 10mb — matches express.json({ limit: '10mb' }).
// Needed by /students/bulk-import and the Razorpay webhook.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = 10 * 1024 * 1024);

var app = builder.Build();

// ─── Pipeline ─────────────────────────────────────────────────────────────────
// Order matters. Exception handling first so it wraps everything below.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseResponseCompression();

// helmet(): CSP and HSTS were explicitly disabled in the Node app
// (contentSecurityPolicy: false, hsts: false) — the API serves JSON, not HTML,
// and Railway terminates TLS. Keep the rest.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();

// Revocation check must sit AFTER authentication (needs a parsed token) and
// BEFORE authorization (a revoked token must not satisfy a policy).
app.UseMiddleware<TokenRevocationMiddleware>();

app.UseAuthorization();

app.MapControllers();

// 404 envelope for unmatched /api routes — port of errorHandler.js notFound.
app.UseApiNotFound();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// ── Extensions bootstrap ─────────────────────────────────────────────────────
// citext/pg_trgm must exist BEFORE the app's pooled connections open: Npgsql
// loads the type catalog per physical connection, so an extension created
// mid-flight is invisible to already-open connections ("NpgsqlDbType 'Citext'
// isn't present"). A separate, throwaway connection creates them first — the
// data source pool hasn't opened anything yet at this point in boot.
await using (var boot = new NpgsqlConnection(connString))
{
    await boot.OpenAsync();
    await using var cmd = boot.CreateCommand();
    cmd.CommandText =
        "CREATE EXTENSION IF NOT EXISTS citext; " +
        "CREATE EXTENSION IF NOT EXISTS pg_trgm;";
    await cmd.ExecuteNonQueryAsync();
}

// ── Migrate on boot ──────────────────────────────────────────────────────────
// Applies any pending committed migrations. Railway deploys self-migrate;
// no-op when up to date. (Requires the Migrations/ folder to be committed —
// `dotnet ef migrations add Initial` locally, see README.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Payroll immutability triggers — applied here instead of inside the
    // migration so a regenerated InitialCreate never silently drops them.
    // Idempotent: CREATE OR REPLACE FUNCTION + DROP TRIGGER IF EXISTS.
    await db.Database.ExecuteSqlRawAsync(
        QMSoft.Api.Data.Configurations.PayrollTriggerSql.UpAll);
}

// Idempotent — safe on every boot, which is what makes it usable as a Railway
// release command. Seed__* provisions the superadmin; Rescue__* is break-glass.
await app.SeedDatabaseAsync();

// Daily late-fee / overdue sweep at 01:00 UTC.
Hangfire.RecurringJob.AddOrUpdate<QMSoft.Api.Features.Jobs.LateFeeJob>(
    "late-fee-sweep", j => j.RunAsync(CancellationToken.None), "0 1 * * *");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
