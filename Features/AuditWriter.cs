using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features;

public interface IAuditWriter
{
    /// <summary>Fire-and-log: an audit failure must never fail the audited request.</summary>
    Task WriteAsync(string action, string? entity = null, string? entityId = null,
                    string? metaJson = null, CancellationToken ct = default);
}

/// <summary>
/// Phase-2 port of middleware/audit.js `logAudit` — the explicit-call form the
/// auth controller uses. The route-level interceptor variant lands in Phase 3;
/// this service is what it will call, so nothing here is throwaway.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditWriter> _log;

    public AuditWriter(AppDbContext db, ITenantContext tenant,
                       IHttpContextAccessor http, ILogger<AuditWriter> log)
    {
        _db = db; _tenant = tenant; _http = http; _log = log;
    }

    public async Task WriteAsync(string action, string? entity = null, string? entityId = null,
                                 string? metaJson = null, CancellationToken ct = default)
    {
        try
        {
            var ctx = _http.HttpContext;

            _db.AuditLogs.Add(new AuditLog
            {
                SchoolId = _tenant.SchoolId,
                UserId = _tenant.UserId,
                Username = ctx?.User.Identity?.Name,
                Role = _tenant.Role,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Ip = ClientIp(ctx),
                UserAgent = ctx?.Request.Headers.UserAgent.ToString(),
                Meta = metaJson,
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Same stance as Node: audit is best-effort, the request succeeds.
            _log.LogError(ex, "Audit write failed for {Action}", action);
        }
    }

    /// <summary>Port of clientIp(req): X-Forwarded-For first hop, else socket.</summary>
    internal static string? ClientIp(HttpContext? ctx)
    {
        if (ctx is null) return null;

        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(fwd))
            return fwd.Split(',')[0].Trim();

        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}
