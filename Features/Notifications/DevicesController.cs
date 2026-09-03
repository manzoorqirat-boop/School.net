using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Notifications;

/// <summary>
/// Device registration for push.
///
/// No privilege gate beyond [Authorize]: every signed-in user may register
/// their own device, and a push privilege would only ever describe who may
/// SEND. The caller can register a token only against themselves — UserId is
/// taken from the JWT, never from the body.
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public DevicesController(AppDbContext db, ITenantContext tenant)
    { _db = db; _tenant = tenant; }

    public sealed class RegisterRequest
    {
        public string? Token { get; set; }
        public string? Platform { get; set; }
    }

    /// <summary>
    /// Upsert by token. Called on every sign-in and whenever Expo hands the app
    /// a rotated token, so it must be cheap and idempotent.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var token = req.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new AppException("Token is required", 400, ErrorCodes.ValidationError);

        // Expo tokens have a fixed shape. Rejecting anything else early keeps
        // raw FCM/APNs tokens — which this send path cannot deliver to — out of
        // the table, where they would fail silently on every notice forever.
        if (!token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
            && !token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
            throw new AppException("Not an Expo push token", 400, ErrorCodes.ValidationError);

        var userId = _tenant.UserId
            ?? throw new AppException("No user on token", 401, ErrorCodes.NotAuthenticated);
        var schoolId = _tenant.SchoolId ?? Guid.Empty;

        var existing = await _db.Set<DeviceToken>()
            .IgnoreQueryFilters()          // the row may belong to another user
            .FirstOrDefaultAsync(d => d.Token == token, ct);

        if (existing is null)
        {
            _db.Add(new DeviceToken
            {
                SchoolId = schoolId, UserId = userId, Token = token,
                Platform = req.Platform, LastSeenAt = DateTime.UtcNow,
            });
        }
        else
        {
            // REASSIGN rather than reject. A shared family tablet changes hands;
            // if the row stayed with the previous user they would keep receiving
            // notices meant for whoever is signed in now.
            existing.UserId = userId;
            existing.SchoolId = schoolId;
            existing.Platform = req.Platform ?? existing.Platform;
            existing.LastSeenAt = DateTime.UtcNow;
            existing.IsRevoked = false;     // a re-register means it is alive again
            existing.RevokedAt = null;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Called on sign-out. Without it, the next person to sign in on a shared
    /// device receives the previous user's notifications until they happen to
    /// register their own token.
    /// </summary>
    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var token = req.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return Ok(new { success = true });

        var row = await _db.Set<DeviceToken>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Token == token, ct);

        // Only the owner may retire it — otherwise any signed-in user could
        // silence anyone else's device by guessing a token.
        if (row is not null && row.UserId == _tenant.UserId)
        {
            row.IsRevoked = true;
            row.RevokedAt = DateTime.UtcNow;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { success = true });
    }
}
