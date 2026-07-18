using System.Text.Json.Serialization;

namespace QMSoft.Api.Domain.Entities;

/// <summary>
/// Per-school privilege OVERRIDES only — no row means config/roles.js defaults
/// (PrivilegeDefaults) apply; that's what `isCustomized` means in
/// GET /api/privileges. Never seed a full matrix per school.
///
/// `privilege` stays text — an open set keyed by string on both sides; a
/// Postgres enum would need a migration per new privilege.
/// </summary>
public class RolePrivilege : IEntity, ITenantScoped
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonPropertyName("schoolId")]
    public Guid SchoolId { get; set; }

    /// <summary>'exam:grade' — the colon survives URL-encoding
    /// (API-CONTRACT §1.11: test PUT /api/privileges/student%3Acreate
    /// through the Railway proxy).</summary>
    public string Privilege { get; set; } = "";

    /// <summary>text[] — `roles @> ARRAY['teacher']` is GIN-indexable.</summary>
    public List<string> Roles { get; set; } = [];

    public Guid? UpdatedByUserId { get; set; }

    /// <summary>Mongo had ONLY updatedAt (no createdAt) — kept faithful.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Highest-volume table. schoolId NULLABLE (superadmin actions) — filtered
/// like User, not via ITenantScoped. Append-only: no updatedAt, no soft
/// delete, and nothing should ever delete audit rows.
///
/// Written by the audit middleware/filter (Phase 3), never by controllers.
/// </summary>
public class AuditLog : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonPropertyName("schoolId")]
    public Guid? SchoolId { get; set; }

    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }

    /// <summary>'student.create', 'fee.collect' — dot-separated, unlike the
    /// colon-separated privileges. Both conventions exist in Node; preserved.</summary>
    public string Action { get; set; } = "";

    public string? Entity { get; set; }

    /// <summary>TEXT, not uuid — deliberately loose in Mongo ("can reference
    /// anything"); casting would break non-uuid refs (SCHEMA-MAP §7).</summary>
    public string? EntityId { get; set; }

    /// <summary>TEXT, deviating from SCHEMA-MAP's inet: proxies can hand
    /// Node's req.ip odd shapes ('unknown', comma lists) and an inet cast
    /// failure would make the AUDIT WRITE abort the audited request. Faithful
    /// and fail-safe beats tidy here.</summary>
    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Mixed → jsonb. Free-form context per action.</summary>
    public string? Meta { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
