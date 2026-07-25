using Hangfire;
using Hangfire.PostgreSql;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
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
dsb.MapEnum<NoticePriority>("notice_priority", EnumMemberNameTranslator.For<NoticePriority>());
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
        // MUST be true. With <Nullable>enable</Nullable>, `false` promotes EVERY
        // non-nullable property to a required body field — including entity
        // properties the client has no reason to send (Student.shareEnabled,
        // Student.isDeleted, Exam.weightInFinal, School.createdAt…). Because
        // controllers bind entities directly, that turned ordinary saves into
        // 400s. Absent field → CLR default; FluentValidation is the real gate.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
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

// [ApiController] short-circuits with RFC7807 ProblemDetails BEFORE any
// middleware runs, so model-binding failures never reached
// ExceptionHandlingMiddleware and arrived at the client as
// { title, errors:{...} } with no `error`/`code` key — surfacing in the app as
// a bare "HTTP 400". Reshape them into the documented envelope instead.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
{
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var details = ctx.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .Select(kv => new
            {
                field = kv.Key.StartsWith("$.", StringComparison.Ordinal) ? kv.Key[2..] : kv.Key,
                message = kv.Value!.Errors[0].ErrorMessage is { Length: > 0 } m
                    ? m
                    : "Invalid value.",
            })
            .ToList();

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
        {
            error = "Validation failed",
            code = ErrorCodes.ValidationError,
            details,
        });
    };
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

// Strip HTML from free-text JSON fields — parity with the Node app's global
// app.use(sanitizeBody). Sits AFTER auth so unauthenticated junk is rejected
// before we spend time rewriting its body, and BEFORE MapControllers so every
// action sees clean input without per-field guards.
app.UseSanitizeBody();

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
    var bootLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Boot");

    // MigrateAsync() applies the migrations COMPILED INTO THIS ASSEMBLY. If the
    // Migrations/ folder was never generated there are zero of them, EF finds
    // nothing pending and logs the very misleading "No migrations were applied.
    // The database is already up to date." — up to date with an empty set.
    //
    // On a database that already has tables (the original deploy) nothing
    // notices. On a FRESH Postgres nothing creates the schema at all, and the
    // first statement to touch a real table dies with 42P01. Check explicitly
    // rather than letting that surface as a confusing trigger error.
    var hasMigrations = db.Database.GetMigrations().Any();

    if (hasMigrations)
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        // No migrations compiled in. Create the schema from the model.
        //
        // NOT EnsureCreated: its probe asks "does this database contain ANY
        // user table in ANY non-system schema", not "do MY tables exist".
        // AddHangfireServer() provisions the `hangfire` schema during host
        // startup — before this block runs — so EnsureCreated saw those tables,
        // concluded the database was already set up, created nothing, and the
        // app's own tables never appeared. The next statement then died with
        // 42P01 on `payrolls`.
        //
        // Compare the FULL model against the database, not one sentinel table.
        //
        // This used to probe for `payrolls` alone and skip the whole script when
        // it existed. A boot that created some tables and died part-way (or a
        // model that gained tables after the first deploy) then left the rest
        // permanently absent: every later boot saw `payrolls`, logged "schema
        // already present", and created nothing. That is exactly how
        // `fee_invoices` went missing while the app otherwise ran fine — the
        // failure only surfaced at the first request that touched fees, as
        // 42P01 masked into a generic 500 by ExceptionHandlingMiddleware.
        //
        // GenerateCreateScript() emits the same SQL EnsureCreated would have,
        // including CREATE TYPE for all 28 Postgres enums. It is re-run in full
        // whenever ANY table is missing; the duplicate-object catch below makes
        // the already-present statements no-ops.
        var expectedTables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> missingTables;
        await using (var probe = new NpgsqlConnection(connString))
        {
            await probe.OpenAsync();
            await using var probeCmd = probe.CreateCommand();
            // relkind 'r' = ordinary table, 'p' = partitioned table.
            probeCmd.CommandText = @"
                SELECT c.relname
                FROM pg_catalog.pg_class c
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')";

            var present = new HashSet<string>(StringComparer.Ordinal);
            await using (var rdr = await probeCmd.ExecuteReaderAsync())
                while (await rdr.ReadAsync())
                    present.Add(rdr.GetString(0));

            missingTables = expectedTables.Where(t => !present.Contains(t)).ToList();
        }

        if (missingTables.Count == 0)
        {
            bootLog.LogInformation(
                "Application schema already present: all {Count} model table(s) exist.",
                expectedTables.Count);
        }
        else
        {
            bootLog.LogWarning(
                "No EF migrations are compiled into this build and {Missing} of {Total} " +
                "model table(s) are missing ({Names}). Creating the schema from the " +
                "model. Generate and commit Migrations/ (dotnet ef migrations add " +
                "InitialCreate) for versioned schema changes.",
                missingTables.Count,
                expectedTables.Count,
                string.Join(", ", missingTables.Take(15))
                    + (missingTables.Count > 15 ? ", …" : ""));

            var ddl = db.Database.GenerateCreateScript();

            // Idempotent-ish: the script is only run when our tables are absent,
            // but a partially-created database (a previous boot that failed
            // halfway) would otherwise stop on the first duplicate. Statements
            // are applied individually so an "already exists" does not abort the
            // rest.
            // Statements are separated by ";" followed by a newline in the
            // generated script. Split on both line-ending styles so the same
            // code works regardless of what the generator emitted.
            var statements = ddl
                .Replace("\r\n", "\n")
                .Split(";\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            // Applied over a RAW Npgsql connection, deliberately not through
            // EF. EF logs every statement at Info and every failure at Error
            // with a full stack trace; a ~180-statement script exceeded
            // Railway's log rate limit ("Messages dropped: 149") and buried the
            // real fatal error. Raw commands produce no EF logging.
            var applied = 0;
            var skipped = 0;
            var failures = new List<string>();

            await using (var ddlConn = new NpgsqlConnection(connString))
            {
                await ddlConn.OpenAsync();

                foreach (var stmt in statements)
                {
                    await using var ddlCmd = ddlConn.CreateCommand();
                    ddlCmd.CommandText = stmt;
                    try
                    {
                        await ddlCmd.ExecuteNonQueryAsync();
                        applied++;
                    }
                    catch (PostgresException ex) when (
                        ex.SqlState is "42P07"    // duplicate_table
                                    or "42710"    // duplicate_object (type, constraint)
                                    or "42P06"    // duplicate_schema
                                    or "42701"    // duplicate_column
                                    or "42P16")   // invalid_table_definition (re-adding a PK)
                    {
                        // Left over from a previous boot that died part-way.
                        skipped++;
                    }
                    catch (PostgresException ex)
                    {
                        // Record and continue: one bad statement must not stop
                        // the other 179. Reported in full afterwards.
                        failures.Add($"[{ex.SqlState}] {ex.MessageText} :: "
                                   + stmt[..Math.Min(stmt.Length, 120)].Replace("\n", " "));
                    }
                }
            }

            bootLog.LogWarning(
                "Schema creation complete: {Applied} applied, {Skipped} already existed, {Failed} failed.",
                applied, skipped, failures.Count);

            // Cap the output — a genuinely broken script would otherwise trip
            // the same rate limit that hid the problem in the first place.
            foreach (var f in failures.Take(30))
                bootLog.LogError("Schema statement failed: {Failure}", f);
            if (failures.Count > 30)
                bootLog.LogError("...and {More} more failed statement(s).", failures.Count - 30);

            // Fail fast if the tables STILL are not there. Limping on means the
            // seeder dies next with a stack trace pointing at the wrong place.
            // Re-check EVERY table that was missing, not just `users`. Verifying
            // one table is what let a partial schema pass as complete.
            await using (var verify = new NpgsqlConnection(connString))
            {
                await verify.OpenAsync();
                await using var verifyCmd = verify.CreateCommand();
                verifyCmd.CommandText = @"
                    SELECT c.relname
                    FROM pg_catalog.pg_class c
                    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')";

                var nowPresent = new HashSet<string>(StringComparer.Ordinal);
                await using (var rdr = await verifyCmd.ExecuteReaderAsync())
                    while (await rdr.ReadAsync())
                        nowPresent.Add(rdr.GetString(0));

                var stillMissing = expectedTables.Where(t => !nowPresent.Contains(t)).ToList();
                if (stillMissing.Count > 0)
                {
                    // Log, do NOT throw. Same stance as the payroll-trigger guard
                    // below: a missing table degrades the features that use it,
                    // but throwing here crash-loops the container and takes the
                    // ENTIRE API offline — including the endpoints that work and
                    // the logs needed to diagnose this.
                    bootLog.LogCritical(
                        "Schema creation ran {Statements} statement(s) ({Applied} applied, " +
                        "{Skipped} skipped, {Failed} failed) but {Count} table(s) still do " +
                        "not exist: {Names}. Requests touching these will fail with 42P01. " +
                        "See the 'Schema statement failed' entries above for the cause.",
                        statements.Count, applied, skipped, failures.Count,
                        stillMissing.Count,
                        string.Join(", ", stillMissing.Take(25))
                            + (stillMissing.Count > 25 ? ", …" : ""));
                }
            }
        }

        // ── Additive column reconciliation ────────────────────────────────
        //
        // Runs on EVERY boot, whether or not any table was missing.
        //
        // GenerateCreateScript only ever CREATEs. A column added to an entity
        // whose table already exists is therefore never applied: the CREATE
        // TABLE comes back 42P07 and is skipped whole, taking the new column
        // with it. The first query touching that column dies with 42703 — and
        // since the seeder runs before the first request is served, that is a
        // boot crash-loop rather than one broken endpoint. `users.dob` is
        // exactly this, and nothing in the previous bootstrap could have
        // caught it.
        //
        // Scope is deliberately the safe half of the problem: columns the model
        // has and the database does not, addable without touching existing
        // rows. Nullable columns, and columns with a default, are emitted as
        // ALTER TABLE ... ADD COLUMN. NOT NULL columns with no default are NOT
        // — there is no correct value for the rows already there — and are
        // reported for a hand-written migration instead.
        //
        // Renames, type changes and drops stay out of scope by design. At this
        // level a rename is indistinguishable from an add plus a drop, and
        // guessing wrong destroys data.
        try
        {
            var tablesNow = new HashSet<string>(StringComparer.Ordinal);
            var existingCols = new HashSet<string>(StringComparer.Ordinal);

            await using (var colConn = new NpgsqlConnection(connString))
            {
                await colConn.OpenAsync();

                await using (var tCmd = colConn.CreateCommand())
                {
                    tCmd.CommandText = @"
                        SELECT c.relname
                        FROM pg_catalog.pg_class c
                        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')";
                    await using var rdr = await tCmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync()) tablesNow.Add(rdr.GetString(0));
                }

                await using (var cCmd = colConn.CreateCommand())
                {
                    cCmd.CommandText = @"
                        SELECT table_name || '.' || column_name
                        FROM information_schema.columns
                        WHERE table_schema = 'public'";
                    await using var rdr = await cCmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync()) existingCols.Add(rdr.GetString(0));
                }
            }

            var toAdd = new List<(string Table, string Sql, string Label)>();
            var needManual = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var et in db.Model.GetEntityTypes())
            {
                var table = et.GetTableName();
                if (string.IsNullOrEmpty(table)) continue;

                // Table absent entirely → the CREATE path owns it, not this one.
                if (!tablesNow.Contains(table)) continue;

                var store = StoreObjectIdentifier.Table(table, et.GetSchema());

                foreach (var prop in et.GetProperties())
                {
                    var col = prop.GetColumnName(store);
                    if (string.IsNullOrEmpty(col)) continue;

                    var key = $"{table}.{col}";
                    // TPH and owned types map several entities onto one table;
                    // each shared column would otherwise be considered twice.
                    if (!seen.Add(key)) continue;
                    if (existingCols.Contains(key)) continue;

                    var type = prop.GetColumnType(store);
                    if (string.IsNullOrEmpty(type))
                    {
                        needManual.Add($"{key} (no resolvable column type)");
                        continue;
                    }

                    // HasDefaultValueSql wins; HasDefaultValue is rendered for
                    // the handful of literal shapes actually used in this model.
                    var def = prop.GetDefaultValueSql(store);
                    if (def is null)
                    {
                        var v = prop.GetDefaultValue(store);
                        def = v switch
                        {
                            null            => null,
                            bool bl         => bl ? "TRUE" : "FALSE",
                            string s        => "'" + s.Replace("'", "''") + "'",
                            IFormattable num when v is int or long or short or decimal or double or float
                                            => num.ToString(null, CultureInfo.InvariantCulture),
                            _               => null,
                        };
                    }

                    if (!prop.IsNullable && def is null)
                    {
                        // Adding this would fail on any non-empty table.
                        needManual.Add($"{key} ({type}, NOT NULL, no default)");
                        continue;
                    }

                    var sql = $"ALTER TABLE \"{table}\" ADD COLUMN IF NOT EXISTS \"{col}\" {type}"
                            + (def is null ? "" : $" DEFAULT {def}")
                            + (prop.IsNullable ? "" : " NOT NULL");

                    toAdd.Add((table, sql, key));
                }
            }

            if (toAdd.Count == 0 && needManual.Count == 0)
            {
                bootLog.LogInformation("Column reconciliation: no additive changes needed.");
            }

            if (toAdd.Count > 0)
            {
                bootLog.LogWarning(
                    "Column reconciliation: adding {Count} missing column(s): {Names}",
                    toAdd.Count, string.Join(", ", toAdd.Select(x => x.Label).Take(25))
                        + (toAdd.Count > 25 ? ", …" : ""));

                await using var alterConn = new NpgsqlConnection(connString);
                await alterConn.OpenAsync();

                foreach (var (_, sql, label) in toAdd)
                {
                    await using var alterCmd = alterConn.CreateCommand();
                    alterCmd.CommandText = sql;
                    try
                    {
                        await alterCmd.ExecuteNonQueryAsync();
                        bootLog.LogInformation("Added column {Column}.", label);
                    }
                    catch (PostgresException ex) when (ex.SqlState == "42701")
                    {
                        // duplicate_column — added concurrently by another
                        // instance booting at the same time. Benign.
                    }
                    catch (PostgresException ex)
                    {
                        // Never fatal: one column that will not apply must not
                        // cost the whole API its boot.
                        bootLog.LogError(
                            "Column add failed for {Column}: [{State}] {Message}",
                            label, ex.SqlState, ex.MessageText);
                    }
                }
            }

            if (needManual.Count > 0)
            {
                bootLog.LogWarning(
                    "Column reconciliation: {Count} column(s) CANNOT be added automatically " +
                    "and need a hand-written migration (existing rows have no value for " +
                    "them): {Names}",
                    needManual.Count, string.Join(", ", needManual.Take(25))
                        + (needManual.Count > 25 ? ", …" : ""));
            }
        }
        catch (Exception ex)
        {
            bootLog.LogError(ex,
                "Column reconciliation failed. The API will still start; any column added " +
                "to the model since the table was created is still missing and will surface " +
                "as 42703 on the first query that touches it.");
        }
    }

    // Payroll immutability triggers — applied here instead of inside the
    // migration so a regenerated InitialCreate never silently drops them.
    // Idempotent: CREATE OR REPLACE FUNCTION + DROP TRIGGER IF EXISTS.
    //
    // Guarded: if the schema is somehow still absent this must not take the
    // whole container down in a crash-loop. A missing trigger degrades payroll
    // immutability; a boot loop takes the entire API offline. Log loudly and
    // keep serving.
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            QMSoft.Api.Data.Configurations.PayrollTriggerSql.UpAll);
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
    {
        bootLog.LogError(ex,
            "Could not install the payroll immutability triggers: the payrolls " +
            "table does not exist. The schema was not created. Check that " +
            "Migrations/ is committed, or that EnsureCreated succeeded.");
    }
}

// Idempotent — safe on every boot, which is what makes it usable as a Railway
// release command. Seed__* provisions the superadmin; Rescue__* is break-glass.
await app.SeedDatabaseAsync();

// Daily late-fee / overdue sweep at 01:00 UTC.
// Use the DI-resolved manager, NOT the static RecurringJob facade — the static
// one reads JobStorage.Current, which isn't populated at this point in boot and
// throws "Current JobStorage instance has not been initialized yet".
using (var scope = app.Services.CreateScope())
{
    var recurring = scope.ServiceProvider.GetRequiredService<Hangfire.IRecurringJobManager>();
    recurring.AddOrUpdate<QMSoft.Api.Features.Jobs.LateFeeJob>(
        "late-fee-sweep", j => j.RunAsync(CancellationToken.None), "0 1 * * *");
}

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
