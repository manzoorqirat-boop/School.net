using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Auth;
using QMSoft.Api.Infrastructure.Crypto;

namespace QMSoft.Api.Controllers;

// ── Request / response shapes ───────────────────────────────────────────────
// Fields are nullable so [ApiController]'s automatic model validation never
// fires (it would emit an ASP.NET ProblemDetails body, not our {error,code}
// envelope). Required-field checks are done by hand below, exactly like the
// Node controller, so the JSON shape and codes stay wire-compatible.

public sealed record LoginRequest(string? Username, string? Password, string? SchoolSlug);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record LogoutRequest(string? RefreshToken, bool AllDevices);
public sealed record ChangePasswordRequest(string? OldPassword, string? NewPassword);
public sealed record VerifyPasswordRequest(string? Password);

/// <summary>
/// Port of controllers/authController.js + routes/auth.js.
///
/// Anonymous endpoints (Login, Refresh) must bypass the global tenant query
/// filter explicitly — an unauthenticated request has no schoolId claim, so
/// AppDbContext's default User filter would only ever match school-less
/// (superadmin) rows. See AppDbContext's "unauthenticated → any row: ✘" case
/// and Data/DatabaseSeeder.cs, which hits the same thing and solves it the
/// same way: IgnoreQueryFilters() + an explicit SchoolId match.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly ITokenRevocationStore _revocation;
    private readonly ICryptoService _crypto;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        AppDbContext db,
        ITokenService tokens,
        ITokenRevocationStore revocation,
        ICryptoService crypto,
        IConfiguration cfg,
        ILogger<AuthController> log)
    {
        _db = db;
        _tokens = tokens;
        _revocation = revocation;
        _crypto = crypto;
        _cfg = cfg;
        _log = log;
    }

    // ── Login ────────────────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    // TODO(rate limiting): Node applied a 30-attempts/15-min IP limiter here
    // (skipSuccessfulRequests: true). Not yet wired in .NET — see README
    // "Not yet built". Apply [EnableRateLimiting("login")] once configured.
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new AppException("username and password required", 400, ErrorCodes.MissingCredentials);

        Guid? schoolId = null;
        if (!string.IsNullOrWhiteSpace(req.SchoolSlug))
        {
            var slug = req.SchoolSlug.Trim().ToLowerInvariant();
            var school = await _db.Schools.FirstOrDefaultAsync(s => s.Slug == slug, ct);
            if (school is null || !school.IsActive)
                throw new AppException("School not found or inactive", 401, ErrorCodes.InvalidCredentials);

            schoolId = school.Id;
        }

        var username = req.Username.Trim().ToLowerInvariant();

        // Anonymous request: IgnoreQueryFilters + explicit SchoolId match,
        // same pattern as DatabaseSeeder.SeedSuperAdminAsync.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.ParentOf)
            .FirstOrDefaultAsync(u => u.Username == username && u.SchoolId == schoolId, ct);

        if (user is null || !user.IsActive)
            throw new AppException("Invalid credentials", 401, ErrorCodes.InvalidCredentials);

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
            throw new AppException("Invalid credentials", 401, ErrorCodes.InvalidCredentials);

        // Backfill schoolSlug if the row is missing it (post-seed / manually
        // created users often omit it).
        if (schoolId.HasValue && string.IsNullOrEmpty(user.SchoolSlug))
        {
            var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, ct);
            if (school is not null) user.SchoolSlug = school.Slug;
        }

        user.LastLoginAt = DateTime.UtcNow;

        var pair = _tokens.Generate(user.Id, user.SchoolId, EnumWire<UserRole>.ToWire(user.Role), user.Username);

        await PruneExpiredRefreshTokensAsync(user.Id, ct);
        _db.UserRefreshTokens.Add(NewRefreshTokenRow(user.Id, pair.RefreshToken));

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(user, "auth.login", "user", user.Id.ToString(), ct);

        return Ok(LoginPayload(pair, user));
    }

    // ── Refresh ──────────────────────────────────────────────────────────

    [HttpPost("refresh")]
    [AllowAnonymous]
    // TODO(rate limiting): Node applied a 60-attempts/15-min IP limiter here.
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            throw new AppException("refreshToken required", 400, ErrorCodes.NoRefreshToken);

        var principal = _tokens.ValidateRefreshToken(req.RefreshToken);
        if (principal is null)
            throw new AppException("Invalid refresh token", 401, ErrorCodes.InvalidRefreshToken);

        if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            throw new AppException("Invalid refresh token", 401, ErrorCodes.InvalidRefreshToken);

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive)
            throw new AppException("User not found or inactive", 401, ErrorCodes.UserNotFound);

        var hash = _crypto.Sha256Hex(req.RefreshToken);
        var present = await _db.UserRefreshTokens
            .AnyAsync(t => t.UserId == user.Id && t.TokenHash == hash, ct);

        if (!present)
            throw new AppException("Refresh token revoked", 401, ErrorCodes.TokenRevoked);

        // Prune expired rows on every refresh, not just at login — otherwise an
        // expired-but-not-yet-pruned token can look "present" for up to 7 days.
        // Done AFTER the present-check so a legitimately-expired token still
        // gets a clean 401 above rather than silently vanishing first.
        await PruneExpiredRefreshTokensAsync(user.Id, ct);

        // Rotation: the presented refresh token is single-use.
        var used = await _db.UserRefreshTokens
            .Where(t => t.UserId == user.Id && t.TokenHash == hash)
            .ToListAsync(ct);
        _db.UserRefreshTokens.RemoveRange(used);

        var pair = _tokens.Generate(user.Id, user.SchoolId, EnumWire<UserRole>.ToWire(user.Role), user.Username);
        _db.UserRefreshTokens.Add(NewRefreshTokenRow(user.Id, pair.RefreshToken));

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            accessToken = pair.AccessToken,
            refreshToken = pair.RefreshToken,
            expiresIn = pair.ExpiresIn,
            tokenType = "Bearer",
            token = pair.AccessToken, // legacy alias
        });
    }

    // ── Logout ───────────────────────────────────────────────────────────

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null)
            throw new AppException("Not authenticated", 401, ErrorCodes.NotAuthenticated);

        if (req.AllDevices)
        {
            var all = await _db.UserRefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(ct);
            _db.UserRefreshTokens.RemoveRange(all);
        }
        else if (!string.IsNullOrEmpty(req.RefreshToken))
        {
            var hash = _crypto.Sha256Hex(req.RefreshToken);
            var matching = await _db.UserRefreshTokens
                .Where(t => t.UserId == user.Id && t.TokenHash == hash)
                .ToListAsync(ct);
            _db.UserRefreshTokens.RemoveRange(matching);
        }

        var raw = ExtractBearerToken(Request);
        if (raw is not null)
        {
            // Immediate Redis blacklist write — a stateless JWT stays valid
            // until its natural expiry otherwise. Best-effort: a Redis blip
            // must not fail the logout itself.
            try { await _revocation.RevokeAsync(raw, ct: ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to revoke access token on logout"); }
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(user, "auth.logout", "user", user.Id.ToString(), ct);

        return Ok(new { ok = true });
    }

    // ── Me ───────────────────────────────────────────────────────────────

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        return Ok(new { user });
    }

    // ── Verify (lightweight token check — frontend guards / tests) ────────

    [HttpPost("verify")]
    [Authorize]
    public IActionResult Verify()
    {
        // Deliberately NOT a full req.user parity with the Node version: that
        // middleware did a DB/Redis lookup per request (name, email, parentOf,
        // ...). TenantContext here is JWT-claims-only by design (see README
        // "Redis user cache — dropped"), so only claims are available without
        // an extra DB round trip. This endpoint is a lightweight liveness
        // check ("is my token still good"), not a profile fetch — use /me for
        // the full safe-JSON user object.
        return Ok(new
        {
            ok = true,
            user = new
            {
                userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                username = User.FindFirst(ClaimTypes.Name)?.Value,
                role = User.FindFirst(ClaimTypes.Role)?.Value,
                schoolId = User.FindFirst(QMSoft.Api.Infrastructure.Tenancy.TenantContext.SchoolIdClaim)?.Value,
            },
        });
    }

    // ── Change password ─────────────────────────────────────────────────

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.OldPassword) || string.IsNullOrEmpty(req.NewPassword))
            throw new AppException("oldPassword and newPassword required", 400, ErrorCodes.ValidationError);

        var user = await CurrentUserAsync(ct);
        if (user is null)
            throw new AppException("User not found", 401, ErrorCodes.UserNotFound);

        var pw = PasswordPolicy.Validate(req.NewPassword, user.Username);
        if (!pw.Ok)
            throw new AppException(pw.Error!, 400, pw.Code!);

        if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.Password))
            throw new AppException("Old password incorrect", 401, ErrorCodes.InvalidCredentials);

        if (req.OldPassword == req.NewPassword)
            throw new AppException("New password must differ from old password", 400, ErrorCodes.PwSame);

        user.Password = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 10);
        user.PasswordChangedAt = DateTime.UtcNow;

        // Password change → every session logged out.
        var all = await _db.UserRefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(ct);
        _db.UserRefreshTokens.RemoveRange(all);

        var raw = ExtractBearerToken(Request);
        if (raw is not null)
        {
            try { await _revocation.RevokeAsync(raw, ct: ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to revoke access token on password change"); }
        }

        await _db.SaveChangesAsync(ct);
        await WriteAuditAsync(user, "auth.password_change", "user", user.Id.ToString(), ct);

        return Ok(new { ok = true });
    }

    // ── Verify current password (attendance save confirmation, etc.) ──────

    [HttpPost("verify-password")]
    [Authorize]
    public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Password))
            throw new AppException("password required", 400, ErrorCodes.ValidationError);

        var user = await CurrentUserAsync(ct);
        if (user is null)
            throw new AppException("User not found", 401, ErrorCodes.UserNotFound);

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
            throw new AppException("Incorrect password", 401, ErrorCodes.InvalidCredentials);

        await WriteAuditAsync(user, "auth.verify_password", "user", user.Id.ToString(), ct);

        return Ok(new { ok = true });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private Task<User?> CurrentUserAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            return Task.FromResult<User?>(null);

        // Normal (filtered) query is correct here: the caller is authenticated,
        // so TenantContext resolves SchoolId from the JWT claim and the global
        // filter matches the caller's own row (or is bypassed for superadmin).
        return _db.Users.Include(u => u.ParentOf).FirstOrDefaultAsync(u => u.Id == id, ct)!;
    }

    private static string? ExtractBearerToken(HttpRequest req)
    {
        var h = req.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(h)) return null;

        var parts = h.Split(' ');
        return parts.Length == 2 && parts[0].Equals("Bearer", StringComparison.Ordinal)
            ? parts[1]
            : null;
    }

    private UserRefreshToken NewRefreshTokenRow(Guid userId, string plaintextRefreshToken) => new()
    {
        UserId = userId,
        TokenHash = _crypto.Sha256Hex(plaintextRefreshToken),
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddSeconds(RefreshExpirySeconds()),
        UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
        Ip = ResolveClientIp(),
    };

    /// <summary>
    /// Port of the Node "prune expired refresh tokens" step run on every
    /// login/refresh. Queues deletes on the change tracker; caller still needs
    /// to call SaveChangesAsync afterwards (each endpoint here does exactly one
    /// SaveChangesAsync per request, matching Node's single user.save()).
    /// </summary>
    private async Task PruneExpiredRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.UserRefreshTokens
            .Where(t => t.UserId == userId && t.ExpiresAt != null && t.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count > 0)
            _db.UserRefreshTokens.RemoveRange(expired);
    }

    private System.Net.IPAddress? ResolveClientIp()
    {
        var fwd = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(fwd))
        {
            var first = fwd.Split(',')[0].Trim();
            if (System.Net.IPAddress.TryParse(first, out var parsed)) return parsed;
        }

        return HttpContext.Connection.RemoteIpAddress;
    }

    /// <summary>Best-effort: an audit write must never fail the request it audits.</summary>
    private async Task WriteAuditAsync(User user, string action, string entity, string entityId, CancellationToken ct)
    {
        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                SchoolId = user.SchoolId,
                UserId = user.Id,
                Username = user.Username,
                Role = EnumWire<UserRole>.ToWire(user.Role),
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Ip = ResolveClientIp()?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                CreatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Audit write failed for {Action}", action);
        }
    }

    private int RefreshExpirySeconds()
    {
        // ITokenService only exposes AccessExpirySeconds; refresh expiry is
        // re-derived from config the same way TokenService parses it, so the
        // stored row's ExpiresAt matches the JWT's own exp claim.
        return ParseExpiry(_cfg["JWT_REFRESH_EXPIRY"], 604800);
    }

    private static int ParseExpiry(string? v, int fallback)
    {
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        v = v.Trim();
        if (int.TryParse(v, out var plain)) return plain;

        var unit = v[^1];
        if (!int.TryParse(v[..^1], out var n)) return fallback;

        return unit switch
        {
            's' => n,
            'm' => n * 60,
            'h' => n * 3600,
            'd' => n * 86400,
            _ => fallback,
        };
    }

    private object LoginPayload(TokenPair pair, User user) => new
    {
        accessToken = pair.AccessToken,
        refreshToken = pair.RefreshToken,
        expiresIn = pair.ExpiresIn,
        tokenType = "Bearer",
        token = pair.AccessToken, // legacy alias — old frontend builds read this
        user,
    };
}
