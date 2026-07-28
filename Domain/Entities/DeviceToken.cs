using System.Text.Json.Serialization;

namespace QMSoft.Api.Domain.Entities;

/// <summary>
/// One row per DEVICE, not per user.
///
/// A parent with a phone and a tablet is two rows; a teacher who reinstalls the
/// app gets a new token and the old one dies. Keying this on the user would
/// silently drop one of their devices every time they signed in elsewhere.
///
/// Tokens are Expo push tokens ("ExponentPushToken[xxxxxxxx]"), not raw FCM or
/// APNs tokens — Expo's service does the fan-out to Google and Apple, so this
/// row is platform-agnostic and `Platform` is kept only for diagnostics.
/// </summary>
public class DeviceToken : TenantEntity
{
    /// <summary>Whose device this is. Push audience resolution starts here.</summary>
    public Guid UserId { get; set; }
    [JsonIgnore] public User User { get; set; } = null!;

    /// <summary>The Expo push token. UNIQUE — see the configuration for why the
    /// uniqueness is global rather than per-user.</summary>
    public string Token { get; set; } = "";

    /// <summary>"ios" | "android" | "web". Diagnostics only; Expo routes by token.</summary>
    public string? Platform { get; set; }

    /// <summary>Refreshed on every register call. A token untouched for months
    /// belongs to an app that was deleted without telling us — Expo will
    /// eventually answer DeviceNotRegistered, but this lets a cleanup job find
    /// them before that.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when Expo reports the token is dead. Kept rather than
    /// deleted so a reinstall on the same device can be told apart from a
    /// device that was never seen.</summary>
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
