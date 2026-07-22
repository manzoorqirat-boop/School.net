using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Auth;
using QMSoft.Api.Infrastructure.Crypto;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Auth;

/// <summary>
/// Port of controllers/authController.js + routes/auth.js. Response shapes are
/// CONTRACT — two corrections to API-CONTRACT §0.8 discovered in the source:
///   • login includes `tokenType: "Bearer"` and does NOT include a `school`
///     object (the frontend loads school context separately);
///   • refresh returns tokens only — no `user`.
/// Faithful to the Node code, not to the earlier doc.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly ITokenRevocationStore _revocation;
    private readonly ICryptoService _crypto;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _cfg;

    public AuthController(
        AppDbContext db, ITokenService tokens, ITokenRevocationStore revocation,
        ICryptoService crypto, ITenantContext tenant, IAuditWriter audit,
        IMemoryCache cache, IConfiguration cfg)
    {
        _db = db; _tokens = tokens; _revocation = revocation; _crypto = crypto;
        _tenant = tenant; _audit = audit; _cache = cache; _cfg = cfg;
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────

    public sealed record LoginRequest(string? Username, string? Password, string? SchoolSlug);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        // Node loginLimiter: per-IP fixed window. 10 attempts / 15 min.
        if (!CheckRateLimit($"login:{AuditWriter.ClientIp(HttpContext)}", limit: 10))
            return StatusCode(429, new { error = "Too many login attempts, try again later", code = "RATE_LIMITED" });

        if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "username and password required", code = "MISSING_CREDENTIALS" });

        // schoolSlug → tenant; absent slug = superadmin login (schoolId NULL).
        Guid? schoolId = null;
        string? schoolSlug = null;
        School? schoolEntity = null;
        if (!string.IsNullOrEmpty(req.SchoolSlug))
        {
            var school = await _db.Schools
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Slug == req.SchoolSlug.ToLowerInvariant(), ct);

            if (school is null || !school.IsActive)
                return Unauthorized(new { error = "School not found or inactive" });   // no code — Node has none

            schoolId = school.Id;
            schoolSlug = school.Slug;
            schoolEntity = school;
        }

        // Unauthenticated ⇒ query filter is active with no tenant ⇒ matches
        // nothing. This is one of the three sanctioned opt-outs (SCHEMA-MAP §10).
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u =>
                u.Username == req.Username.ToLowerInvariant() && u.SchoolId == schoolId, ct);

        if (user is null || !user.IsActive ||
            !BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
            return Unauthorized(new { error = "Invalid credentials", code = "INVALID_CREDENTIALS" });

        if (schoolId is not null && string.IsNullOrEmpty(user.SchoolSlug))
            user.SchoolSlug = schoolSlug;

        user.LastLoginAt = DateTime.UtcNow;

        var pair = IssueTokens(user);
        StoreRefreshToken(user, pair.RefreshToken);
        PruneExpired(user);

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("auth.login", "user", user.Id.ToString(), ct: ct);

        return Ok(new
        {
            accessToken = pair.AccessToken,
            refreshToken = pair.RefreshToken,
            expiresIn = pair.ExpiresIn,
            tokenType = "Bearer",
            token = pair.AccessToken,            // legacy field — api.ts reads either
            user = ToSafeJson(user),

            // auth.tsx does `setSession(token, res.user, res.school, …)` and
            // then `setSchool(res.school ?? null)`. Without this field the
            // client stored school = null, so every `school._id` read was
            // undefined — school-setup PUT went to /api/schools/undefined and
            // 404'd. Superadmin logs in without a slug, so this is null there.
            school = schoolEntity,
        });
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────

    public sealed record RefreshRequest(string? RefreshToken);

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? req, CancellationToken ct)
    {
        if (!CheckRateLimit($"refresh:{AuditWriter.ClientIp(HttpContext)}", limit: 60))
            return StatusCode(429, new { error = "Too many requests, try again later", code = "RATE_LIMITED" });

        var raw = req?.RefreshToken;
        if (string.IsNullOrEmpty(raw))
            return BadRequest(new { error = "refreshToken required", code = ErrorCodes.NoRefreshToken });

        var principal = _tokens.ValidateRefreshToken(raw);
        var userIdStr = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (principal is null || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid refresh token", code = "INVALID_REFRESH_TOKEN" });

        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive)
            return Unauthorized(new { error = "User not found or inactive", code = "USER_NOT_FOUND" });

        // Signature valid is necessary, not sufficient: the HASH must be on file.
        // A rotated-away or logged-out token fails here — that's what makes
        // rotation real.
        var hash = _crypto.Sha256Hex(raw);
        var stored = user.RefreshTokens.FirstOrDefault(rt => rt.TokenHash == hash);
        if (stored is null)
            return Unauthorized(new { error = "Refresh token revoked", code = ErrorCodes.TokenRevoked });

        PruneExpired(user);
        user.RefreshTokens.Remove(stored);              // rotation: old token dies

        var pair = IssueTokens(user);
        StoreRefreshToken(user, pair.RefreshToken);

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            accessToken = pair.AccessToken,
            refreshToken = pair.RefreshToken,
            expiresIn = pair.ExpiresIn,
            tokenType = "Bearer",
            token = pair.AccessToken,                    // legacy — NO user field
        });
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────

    public sealed record LogoutRequest(string? RefreshToken, bool? AllDevices);

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? req, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { error = "Not authenticated" });

        if (req?.AllDevices == true)
            user.RefreshTokens.Clear();
        else if (!string.IsNullOrEmpty(req?.RefreshToken))
        {
            var hash = _crypto.Sha256Hex(req.RefreshToken);
            var t = user.RefreshTokens.FirstOrDefault(rt => rt.TokenHash == hash);
            if (t is not null) user.RefreshTokens.Remove(t);
        }

        // Kill the ACCESS token for its remaining lifetime. The Node app also
        // kept a MongoDB invalidatedTokens[] fallback for when Redis was down;
        // that column was dropped by design (SCHEMA-MAP §4.2) — the store's
        // fail-open behaviour is the documented trade-off.
        await RevokeCurrentAccessTokenAsync(ct);

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("auth.logout", "user", user.Id.ToString(), ct: ct);

        return Ok(new { ok = true });
    }

    // ── GET /api/auth/me · POST /api/auth/verify ──────────────────────────

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);

        // Also return the school so a session created before login included it
        // can self-heal on the next /me call, without forcing a re-login.
        School? school = null;
        if (_tenant.SchoolId is { } sid)
            school = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid, ct);

        return Ok(new { user = user is null ? null : ToSafeJson(user), school });
    }

    [HttpPost("verify")]
    [Authorize]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        return Ok(new { ok = true, user = user is null ? null : ToSafeJson(user) });
    }

    // ── POST /api/auth/change-password ────────────────────────────────────

    public sealed record ChangePasswordRequest(string? OldPassword, string? NewPassword);

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.OldPassword) || string.IsNullOrEmpty(req.NewPassword))
            return BadRequest(new { error = "oldPassword and newPassword required" });

        var user = await CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { error = "User not found" });

        var check = PasswordPolicy.Validate(req.NewPassword, user.Username);
        if (!check.Ok)
            return BadRequest(new { error = check.Error, code = check.Code });

        if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.Password))
            return Unauthorized(new { error = "Old password incorrect" });

        if (req.OldPassword == req.NewPassword)
            return BadRequest(new { error = "New password must differ from old password", code = "PW_SAME" });

        user.Password = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 10);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.RefreshTokens.Clear();                     // all sessions logged out

        await RevokeCurrentAccessTokenAsync(ct);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("auth.password_change", "user", user.Id.ToString(), ct: ct);

        return Ok(new { ok = true });
    }

    // ── POST /api/auth/verify-password ────────────────────────────────────

    public sealed record VerifyPasswordRequest(string? Password);

    [HttpPost("verify-password")]
    [Authorize]
    public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "password required" });

        var user = await CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { error = "User not found" });

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
            return Unauthorized(new { error = "Incorrect password" });

        await _audit.WriteAsync("auth.verify_password", "user", user.Id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private TokenPair IssueTokens(User user) =>
        _tokens.Generate(user.Id, user.SchoolId,
                         EnumWireBridge.RoleToWire(user.Role), user.Username, user.StudentId);

    private void StoreRefreshToken(User user, string rawToken)
    {
        System.Net.IPAddress.TryParse(AuditWriter.ClientIp(HttpContext), out var ip);

        user.RefreshTokens.Add(new UserRefreshToken
        {
            TokenHash = _crypto.Sha256Hex(rawToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(RefreshExpirySeconds()),
            UserAgent = Request.Headers.UserAgent.ToString(),
            Ip = ip,
        });
    }

    private static void PruneExpired(User user)
    {
        var now = DateTime.UtcNow;
        foreach (var t in user.RefreshTokens.Where(rt => rt.ExpiresAt <= now).ToList())
            user.RefreshTokens.Remove(t);
    }

    private int RefreshExpirySeconds()
    {
        var v = _cfg["JWT_REFRESH_EXPIRY"];
        if (string.IsNullOrWhiteSpace(v)) return 7 * 86400;
        v = v.Trim();
        if (int.TryParse(v, out var plain)) return plain;
        if (!int.TryParse(v[..^1], out var n)) return 7 * 86400;
        return v[^1] switch { 's' => n, 'm' => n * 60, 'h' => n * 3600, 'd' => n * 86400, _ => 7 * 86400 };
    }

    private async Task<User?> CurrentUserAsync(CancellationToken ct)
    {
        if (_tenant.UserId is not { } id) return null;
        return await _db.Users
            .IgnoreQueryFilters()      // own row, by id from the verified JWT
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    private async Task RevokeCurrentAccessTokenAsync(CancellationToken ct)
    {
        var h = Request.Headers.Authorization.ToString();
        var parts = h.Split(' ');
        if (parts.Length == 2 && parts[0] == "Bearer")
            await _revocation.RevokeAsync(parts[1],
                TimeSpan.FromSeconds(_tokens.AccessExpirySeconds), ct);
    }

    /// <summary>
    /// Fixed-window limiter (per-instance IMemoryCache) — Node's
    /// loginLimiter/refreshLimiter equivalents. Window 15 min.
    /// </summary>
    private bool CheckRateLimit(string key, int limit)
    {
        var count = _cache.GetOrCreate($"rl:{key}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return 0;
        });
        if (count >= limit) return false;
        _cache.Set($"rl:{key}", count + 1, TimeSpan.FromMinutes(15));
        return true;
    }

    /// <summary>
    /// toSafeJSON() equivalent. Password/RefreshTokens are [JsonIgnore] on the
    /// entity, so serializing `user` directly is already safe — this exists to
    /// pin the exact field set the Node payload carried.
    /// </summary>
    private static object ToSafeJson(User u) => new
    {
        _id = u.Id,
        schoolId = u.SchoolId,
        schoolSlug = u.SchoolSlug,
        username = u.Username,
        role = EnumWireBridge.RoleToWire(u.Role),
        name = u.Name,
        fullName = u.FullName,
        email = u.Email,
        phone = u.Phone,
        studentId = u.StudentId,
        parentOf = u.ParentOf.Select(s => s.Id),
        isActive = u.IsActive,
        lastLoginAt = u.LastLoginAt,
        createdAt = u.CreatedAt,
        updatedAt = u.UpdatedAt,
    };
}

/// <summary>Role enum ↔ wire string for JWT claims (must match PrivilegeDefaults keys).</summary>
public static class EnumWireBridge
{
    public static string RoleToWire(UserRole r) => r switch
    {
        UserRole.SuperAdmin => "superadmin",
        UserRole.SchoolAdmin => "school_admin",
        UserRole.Principal => "principal",
        UserRole.Accountant => "accountant",
        UserRole.Teacher => "teacher",
        UserRole.Parent => "parent",
        UserRole.Student => "student",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };
}
