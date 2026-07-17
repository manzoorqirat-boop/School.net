using System.Text.Json.Serialization;

namespace QMSoft.Api.Domain.Entities;

/// <summary>
/// CONTRACT: the frontend reads `_id` on every entity (src/lib/contracts.ts).
/// No JsonNamingPolicy fixes this — it is not a casing difference.
/// Every entity exposed over the wire must carry [JsonPropertyName("_id")].
/// </summary>
public interface IEntity
{
    Guid Id { get; set; }
}

public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>Hard deletes are never performed. DELETE endpoints set the flag.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>Every entity except School. Drives the global query filter.</summary>
public interface ITenantScoped
{
    Guid SchoolId { get; set; }
}

/// <summary>
/// Base for tenant-scoped, audited entities.
/// Soft-delete is opt-in per entity (ISoftDeletable) — not everything has it in
/// the Mongo models, and adding it where it doesn't exist changes behaviour.
/// </summary>
public abstract class TenantEntity : IEntity, ITenantScoped, IAuditable
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonPropertyName("schoolId")]
    public Guid SchoolId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
