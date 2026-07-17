using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Infrastructure.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);

public interface ITokenService
{
    TokenPair Generate(Guid userId, Guid? schoolId, string role, string username);
    ClaimsPrincipal? ValidateRefreshToken(string token);
    int AccessExpirySeconds { get; }
}

/// <summary>
/// Port of utils/jwt.js.
///   access  : JWT_EXPIRY         (default '15m')
///   refresh : JWT_REFRESH_EXPIRY (default '7d')
///
/// Two separate secrets, as in the original. Refresh tokens are additionally
/// stored SHA-256-hashed server-side (user_refresh_tokens.token_hash) — a valid
/// signature is necessary but not sufficient; the hash must also be present,
/// which is what makes rotation and revocation real.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly SymmetricSecurityKey _accessKey;
    private readonly SymmetricSecurityKey _refreshKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessSeconds;
    private readonly int _refreshSeconds;

    public int AccessExpirySeconds => _accessSeconds;

    public TokenService(IConfiguration cfg)
    {
        var accessSecret = cfg["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is not set.");
        var refreshSecret = cfg["JWT_REFRESH_SECRET"]
            ?? throw new InvalidOperationException("JWT_REFRESH_SECRET is not set.");

        // HS256 needs >= 256 bits of key material; a short secret throws at sign
        // time with a confusing message, so fail loudly at boot instead.
        if (Encoding.UTF8.GetByteCount(accessSecret) < 32)
            throw new InvalidOperationException("JWT_SECRET must be at least 32 bytes.");
        if (Encoding.UTF8.GetByteCount(refreshSecret) < 32)
            throw new InvalidOperationException("JWT_REFRESH_SECRET must be at least 32 bytes.");

        _accessKey  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(accessSecret));
        _refreshKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(refreshSecret));
        _issuer     = cfg["JWT_ISSUER"]   ?? "qmsoft";
        _audience   = cfg["JWT_AUDIENCE"] ?? "qmsoft";

        _accessSeconds  = ParseExpiry(cfg["JWT_EXPIRY"], 900);            // 15m
        _refreshSeconds = ParseExpiry(cfg["JWT_REFRESH_EXPIRY"], 604800); // 7d
    }

    public TokenPair Generate(Guid userId, Guid? schoolId, string role, string username)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // Superadmin has no school — the claim is absent, not empty.
        // TenantContext.SchoolId stays null and IsFilterActive goes false.
        if (schoolId.HasValue)
            claims.Add(new Claim(TenantContext.SchoolIdClaim, schoolId.Value.ToString()));

        var access = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(_accessSeconds),
            signingCredentials: new SigningCredentials(_accessKey, SecurityAlgorithms.HmacSha256));

        // Refresh token carries only identity — no role, no school. Role changes
        // must not survive a refresh; the new access token is minted from the DB.
        var refresh = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: now,
            expires: now.AddSeconds(_refreshSeconds),
            signingCredentials: new SigningCredentials(_refreshKey, SecurityAlgorithms.HmacSha256));

        var h = new JwtSecurityTokenHandler();
        return new TokenPair(h.WriteToken(access), h.WriteToken(refresh), _accessSeconds);
    }

    public ClaimsPrincipal? ValidateRefreshToken(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _refreshKey,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses '15m' / '7d' / '900' the way the Node `parseExpirySeconds` did.</summary>
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
}
