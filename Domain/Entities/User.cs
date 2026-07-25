using System.Text.Json.Serialization;

namespace QMSoft.Api.Domain.Entities;

/// <summary>
/// Staff, parents and students all live here. A teacher IS a User with
/// role='teacher' — there is no Teacher entity (payrollController.js:78 proves
/// it: `{ _id: teacherId, schoolId, role: 'teacher' }`).
///
/// User.teacherId → ref:'Teacher' is DROPPED: it referenced a model that never
/// existed and was never read or written. See API-CONTRACT §1.8.
/// </summary>
public class User : IEntity, IAuditable
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    /// <summary>
    /// NULLABLE — superadmin has no school. Not ITenantScoped for that reason:
    /// the interface mandates a non-null Guid, and the global filter would then
    /// hide every superadmin row from itself.
    ///
    /// The filter is applied to User manually in AppDbContext via a nullable-aware
    /// expression. See UserConfiguration.
    /// </summary>
    public Guid? SchoolId { get; set; }

    [JsonIgnore] public School? School { get; set; }

    /// <summary>Denormalised for login-by-slug. Kept in sync on write.</summary>
    public string? SchoolSlug { get; set; }

    // ── Credentials ───────────────────────────────────────────────────────

    /// <summary>citext — lowercased on write AND case-insensitive on read (§4.1).</summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// bcrypt hash, cost 10.
    ///
    /// [JsonIgnore] replaces toSafeJSON()'s `delete obj.password`. The Mongo
    /// approach is opt-OUT: forget to call toSafeJSON() on one code path and the
    /// hash ships to the client. This is opt-IN — the property cannot be
    /// serialized at all, from any path, ever.
    /// </summary>
    [JsonIgnore]
    public string Password { get; set; } = "";

    public UserRole Role { get; set; }

    // ── Profile ───────────────────────────────────────────────────────────
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>
    /// Staff date of birth, for the birthday widget. Nullable and never
    /// required — most existing rows will not have it.
    ///
    /// ⚠️ This column is NOT created by the boot-time schema bootstrap, which
    /// only ever CREATEs missing tables and never ALTERs existing ones. `users`
    /// already exists, so its CREATE TABLE is skipped as a duplicate and this
    /// column with it. Apply migrations/003_notices_birthdays.sql by hand.
    /// </summary>
    public DateOnly? Dob { get; set; }

    /// <summary>
    /// Present because timetable/page.tsx reads `teacher.name || teacher.fullName`.
    /// The Mongo User has no fullName virtual — the frontend is defensively
    /// coding against a field that never arrives. Kept as a computed alias so the
    /// fallback resolves instead of hitting `|| t.email`.
    /// </summary>
    [JsonPropertyName("fullName")]
    public string FullName => Name;

    // ── Links ─────────────────────────────────────────────────────────────

    /// <summary>Set when role='student'. Drives row-level scoping (API-CONTRACT §1.1).</summary>
    public Guid? StudentId { get; set; }

    [JsonIgnore] public Student? Student { get; set; }

    /// <summary>
    /// Many-to-many User↔Student. Drives parent row-scoping: GET /api/students
    /// filters to this set for role='parent', returning an EMPTY PAGE (not 403)
    /// when it's empty.
    ///
    /// Serialized as a flat Guid[] to match Mongo's `parentOf: [ObjectId]`.
    /// </summary>
    [JsonIgnore]
    public ICollection<Student> ParentOf { get; set; } = [];

    [JsonPropertyName("parentOf")]
    public IEnumerable<Guid> ParentOfIds => ParentOf.Select(s => s.Id);

    // ── State ─────────────────────────────────────────────────────────────
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>
    /// Child table, not JSONB — rotation rewrites on every refresh, expiry needs
    /// DELETE WHERE expires_at < now(), revokeAllTokens needs a bulk delete.
    /// [JsonIgnore] replaces `delete obj.refreshTokens`.
    ///
    /// invalidatedTokens[] is DROPPED — it was the DB half of a blacklist that
    /// already moved to Redis (qms:revoked:&lt;sha256&gt;).
    /// </summary>
    [JsonIgnore]
    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = [];

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Domain ────────────────────────────────────────────────────────────

    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;

    /// <summary>
    /// A password change must invalidate every issued refresh token — otherwise
    /// an attacker who stole one keeps minting access tokens after the victim
    /// "secures" the account.
    /// </summary>
    public void RevokeAllRefreshTokens() => RefreshTokens.Clear();
}

/// <summary>
/// One issued refresh token, stored SHA-256 HASHED.
///
/// The plaintext goes to the client exactly once at login/refresh. A DB leak
/// therefore cannot resurrect live sessions — the same reason the Node model
/// hashes them.
///
/// The Node helpers tolerated both hashed and plaintext rows for in-place
/// migration. No production data ⇒ that fallback is dropped. Hash only.
/// </summary>
public class UserRefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>sha256 hex of the plaintext token.</summary>
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Postgres inet. Handles both IPv4 and IPv6 without a length guess.</summary>
    public System.Net.IPAddress? Ip { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
