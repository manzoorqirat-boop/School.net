using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QMSoft.Api.Common;

namespace QMSoft.Api.Infrastructure.Auth;

/// <summary>
/// THE critical piece of the auth port.
///
/// src/lib/api.ts branches on the error code:
///   401 + TOKEN_EXPIRED  → _tryRefresh() then retry the request ONCE
///   401 + INVALID_TOKEN  → clearSession() + window.location.href = '/'
///   401 + TOKEN_REVOKED  → clearSession() + redirect
///
/// Get this backwards and every user is kicked to the login page every 15
/// minutes when their access token expires — the exact "portal flashes then
/// redirects to login" bug the Node codebase already fixed once.
///
/// ASP.NET Core's default 401 is an empty body with a WWW-Authenticate header.
/// That has no `code`, so api.ts would fall through to a generic ApiError and
/// never refresh. Every 401 must be written by hand here.
/// </summary>
public static class JwtEvents
{
    public static JwtBearerEvents Create() => new()
    {
        // Fires when no token, an invalid token, or an expired token is present.
        OnChallenge = async ctx =>
        {
            // Suppress the default empty 401 + WWW-Authenticate response.
            ctx.HandleResponse();

            if (ctx.Response.HasStarted) return;

            var (code, message) = Classify(ctx.AuthenticateFailure, ctx.Request);

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";

            var body = new Dictionary<string, object?>
            {
                ["error"] = message,
                ["code"] = code,
            };

            // errorHandler.js includes expiredAt on TokenExpiredError.
            if (ctx.AuthenticateFailure is SecurityTokenExpiredException ex)
                body["expiredAt"] = ex.Expires;

            await ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
        },

        // Fires when a token IS valid but the policy denies access.
        // Distinct from 401 — api.ts must NOT try to refresh on this.
        OnForbidden = async ctx =>
        {
            if (ctx.Response.HasStarted) return;

            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Insufficient privileges",
                code = ErrorCodes.Forbidden,
            }));
        },
    };

    private static (string Code, string Message) Classify(
        Exception? failure, HttpRequest req)
    {
        // No Authorization header at all → NO_TOKEN (not INVALID_TOKEN).
        // api.ts hard-logs-out on INVALID_TOKEN; a missing header on a public
        // page shouldn't nuke a session that might still be valid.
        if (failure is null)
        {
            var h = req.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(h)) return (ErrorCodes.NoToken, "No token provided");

            // Present but not "Bearer <token>" — malformed, treat as absent.
            var parts = h.Split(' ');
            if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.Ordinal))
                return (ErrorCodes.NoToken, "No token provided");

            return (ErrorCodes.InvalidToken, "Invalid token");
        }

        return failure switch
        {
            // THE branch that matters. Must be checked before the generic case —
            // SecurityTokenExpiredException derives from SecurityTokenValidationException.
            SecurityTokenExpiredException => (ErrorCodes.TokenExpired, "Token expired"),

            SecurityTokenInvalidSignatureException => (ErrorCodes.InvalidToken, "Invalid token"),
            SecurityTokenInvalidIssuerException    => (ErrorCodes.InvalidToken, "Invalid token"),
            SecurityTokenInvalidAudienceException  => (ErrorCodes.InvalidToken, "Invalid token"),
            SecurityTokenNotYetValidException      => (ErrorCodes.InvalidToken, "Invalid token"),
            SecurityTokenMalformedException        => (ErrorCodes.InvalidToken, "Invalid token"),

            _ => (ErrorCodes.InvalidToken, "Invalid token"),
        };
    }
}
