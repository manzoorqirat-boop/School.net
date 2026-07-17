using System.Text.Json;
using QMSoft.Api.Common;
using QMSoft.Api.Infrastructure.Auth;

namespace QMSoft.Api.Middleware;

/// <summary>
/// JWTs are stateless: a signature stays valid until expiry, so logout alone
/// can't kill an access token. middleware/auth.js checked a Redis blacklist on
/// every request; this is that check.
///
/// Placement is load-bearing:
///   UseAuthentication()  → token parsed, principal built
///   THIS                 → revoked? then 401 TOKEN_REVOKED
///   UseAuthorization()   → policies evaluated
///
/// Emits TOKEN_REVOKED, which api.ts treats as a hard logout (no refresh
/// attempt) — correct, since the refresh token was killed at the same time.
/// </summary>
public sealed class TokenRevocationMiddleware
{
    private readonly RequestDelegate _next;

    public TokenRevocationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ITokenRevocationStore store)
    {
        // Anonymous endpoints (login, refresh, public share link, webhook) have
        // no principal — nothing to revoke.
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            await _next(ctx);
            return;
        }

        var raw = Extract(ctx.Request);
        if (raw is null)
        {
            await _next(ctx);
            return;
        }

        if (await store.IsRevokedAsync(raw, ctx.RequestAborted))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Token revoked",
                code = ErrorCodes.TokenRevoked,
            }));
            return;
        }

        await _next(ctx);
    }

    private static string? Extract(HttpRequest req)
    {
        var h = req.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(h)) return null;

        var parts = h.Split(' ');
        return parts.Length == 2 && parts[0].Equals("Bearer", StringComparison.Ordinal)
            ? parts[1]
            : null;
    }
}
