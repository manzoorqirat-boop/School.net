using Microsoft.Extensions.Caching.Distributed;
using QMSoft.Api.Infrastructure.Crypto;

namespace QMSoft.Api.Infrastructure.Auth;

public interface ITokenRevocationStore
{
    Task RevokeAsync(string rawToken, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<bool> IsRevokedAsync(string rawToken, CancellationToken ct = default);
}

/// <summary>
/// Port of the Redis blacklist in middleware/auth.js: qms:revoked:&lt;sha256(token)&gt;.
///
/// This is the ONE cache worth keeping from the Node app (SCHEMA-MAP §4.2).
/// The user/school caches were a Mongoose workaround and are dropped; this one
/// is a correctness mechanism — without it, a logged-out access token stays
/// valid until it expires, because JWTs are stateless.
///
/// TTL = the token's remaining lifetime. Storing longer wastes memory; storing
/// shorter re-validates a token the user explicitly killed.
///
/// Backed by IDistributedCache: Redis in production, in-memory in dev.
/// </summary>
public sealed class TokenRevocationStore : ITokenRevocationStore
{
    private const string Prefix = "qms:revoked:";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(900);

    private readonly IDistributedCache _cache;
    private readonly ICryptoService _crypto;
    private readonly ILogger<TokenRevocationStore> _log;

    public TokenRevocationStore(
        IDistributedCache cache,
        ICryptoService crypto,
        ILogger<TokenRevocationStore> log)
    {
        _cache = cache;
        _crypto = crypto;
        _log = log;
    }

    private string Key(string rawToken) => Prefix + _crypto.Sha256Hex(rawToken);

    public async Task RevokeAsync(string rawToken, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            await _cache.SetStringAsync(
                Key(rawToken), "1",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl,
                },
                ct);
        }
        catch (Exception ex)
        {
            // A failed revoke is a security event, not a 500 for the user —
            // logout should still succeed client-side. Log loudly.
            _log.LogError(ex, "Failed to revoke token — it stays valid until expiry.");
        }
    }

    public async Task<bool> IsRevokedAsync(string rawToken, CancellationToken ct = default)
    {
        try
        {
            return await _cache.GetStringAsync(Key(rawToken), ct) is not null;
        }
        catch (Exception ex)
        {
            // Cache down. FAIL OPEN: treat as not-revoked.
            //
            // This is a deliberate trade-off and matches the Node original's
            // "graceful degradation" comment. Failing closed would 401 every
            // request in the fleet the moment Redis blips — an outage far worse
            // than the narrow window where a logged-out token still works until
            // its 15-minute expiry.
            _log.LogWarning(ex, "Revocation check failed — failing open.");
            return false;
        }
    }
}
