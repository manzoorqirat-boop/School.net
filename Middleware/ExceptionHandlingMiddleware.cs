using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QMSoft.Api.Common;

namespace QMSoft.Api.Middleware;

/// <summary>
/// Port of middleware/errorHandler.js.
///
/// Envelope (CONTRACT — api.ts parses this):
///   { "error": "<message>", "code": "<CODE>", "details": [...]?, ...extras }
///
/// api.ts reads:
///   data.error            → toast text
///   data.details[0].message → appended to toast
///   data.code             → drives refresh-vs-logout
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> log,
        IHostEnvironment env)
    {
        _next = next;
        _log = log;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            await HandleAsync(ctx, ex);
        }
    }

    private async Task HandleAsync(HttpContext ctx, Exception ex)
    {
        if (ctx.Response.HasStarted)
        {
            // Can't rewrite the envelope now — log and let it die.
            _log.LogError(ex, "Exception after response started: {Path}", ctx.Request.Path);
            throw ex;
        }

        var (status, code, message, extras, details) = Map(ex);

        _log.Log(
            status >= 500 ? LogLevel.Error : LogLevel.Warning,
            ex,
            "{Code} {Status} {Method} {Path}",
            code, status, ctx.Request.Method, ctx.Request.Path);

        var body = new JsonObject
        {
            // errorHandler.js masks 500s in production; everything else is verbatim.
            ["error"] = status >= 500 && _env.IsProduction() ? "Internal server error" : message,
            ["code"] = code,
        };

        if (details is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var d in details)
            {
                arr.Add(new JsonObject
                {
                    ["field"] = d.Field,
                    ["message"] = d.Message,
                });
            }
            body["details"] = arr;
        }

        if (extras is not null)
        {
            foreach (var (k, v) in extras)
                body[k] = v is null ? null : JsonValue.Create(v);
        }

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(body.ToJsonString());
    }

    private static (int Status, string Code, string Message,
                    IReadOnlyDictionary<string, object?>? Extras,
                    IReadOnlyList<ValidationDetail>? Details)
        Map(Exception ex) => ex switch
    {
        AppException app =>
            (app.StatusCode, app.Code, app.Message, app.Extras, null),

        // FluentValidation — mirrors Joi's abortEarly:false (ALL errors, not just first)
        ValidationException v =>
            (400, ErrorCodes.ValidationError, "Validation failed", null,
             v.Errors.Select(e => new ValidationDetail
             {
                 Field = ToCamelCase(e.PropertyName),
                 Message = e.ErrorMessage,
             }).ToList()),

        // Npgsql 23505 = unique_violation → Mongo's E11000 equivalent
        DbUpdateException { InnerException: PostgresException { SqlState: "23505" } pg } =>
            (409, ErrorCodes.DuplicateError,
             $"{ExtractField(pg)} already exists",
             new Dictionary<string, object?> { ["field"] = ExtractField(pg) },
             null),

        // 23503 = foreign_key_violation
        DbUpdateException { InnerException: PostgresException { SqlState: "23503" } } =>
            (400, ErrorCodes.InvalidId, "Referenced record does not exist", null, null),

        // 23514 = check_violation (e.g. payroll immutability trigger)
        DbUpdateException { InnerException: PostgresException { SqlState: "23514" } pg2 } =>
            (400, ErrorCodes.ValidationError, pg2.MessageText, null, null),

        // P0001 = raise_exception (our payroll guard trigger)
        DbUpdateException { InnerException: PostgresException { SqlState: "P0001" } pg3 } =>
            (409, ErrorCodes.DuplicateError, pg3.MessageText, null, null),

        DbUpdateConcurrencyException =>
            (409, ErrorCodes.DuplicateError, "Record was modified by another request", null, null),

        _ => (500, ErrorCodes.InternalError, ex.Message, null, null),
    };

    /// <summary>
    /// Best-effort field name out of a Postgres unique-violation.
    /// errorHandler.js does `Object.keys(err.keyPattern)[0]`; Npgsql gives us a
    /// constraint name like "uq_payments_receipt" or "IX_users_school_id_username".
    /// The frontend only surfaces `error` text, so an imperfect guess is fine —
    /// but never throw from here.
    /// </summary>
    private static string ExtractField(PostgresException pg)
    {
        if (!string.IsNullOrEmpty(pg.ColumnName)) return ToCamelCase(pg.ColumnName);

        var c = pg.ConstraintName;
        if (string.IsNullOrEmpty(c)) return "field";

        // uq_payments_receipt_no → receiptNo ; IX_users_school_id_username → username
        var parts = c.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var tail = parts.SkipWhile(p =>
            p is "uq" or "ux" or "ix" or "IX" or "pk" or "fk" ||
            p.All(char.IsUpper)).ToArray();

        return tail.Length > 0 ? ToCamelCase(string.Join('_', tail)) : c;
    }

    private static string ToCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // snake_case → camelCase
        if (s.Contains('_'))
        {
            var parts = s.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(
                parts[0].ToLowerInvariant(),
                string.Concat(parts.Skip(1).Select(p =>
                    char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant())));
        }

        // PascalCase → camelCase (FluentValidation property paths)
        return char.ToLowerInvariant(s[0]) + s[1..];
    }
}

/// <summary>404 for unmatched routes — port of errorHandler.js `notFound`.</summary>
public static class NotFoundEndpoint
{
    public static IApplicationBuilder UseApiNotFound(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            await next();

            if (ctx.Response.StatusCode == StatusCodes.Status404NotFound
                && !ctx.Response.HasStarted
                && ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Endpoint not found",
                    code = ErrorCodes.NotFound,
                    path = ctx.Request.Path.Value,
                    method = ctx.Request.Method,
                }));
            }
        });
}
