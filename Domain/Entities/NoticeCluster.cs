using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<NoticePriority>))]
public enum NoticePriority
{
    [EnumMember(Value = "normal")]    Normal,
    [EnumMember(Value = "important")] Important,
    [EnumMember(Value = "urgent")]    Urgent,
}

/// <summary>
/// School notice board. Targeted along two independent axes:
///
///   TargetRoles   — WHO sees it (empty = every role).
///   TargetClasses — WHICH classes see it (empty = every class).
///
/// The two are ANDed, and the class axis only constrains the roles that
/// actually belong to a class: student and parent. A teacher targeted by role
/// sees a class-targeted notice regardless of which classes they teach —
/// otherwise "Class 5 parents' meeting, teachers please attend" would be
/// invisible to the staff expected to run it.
/// </summary>
public class Notice : TenantEntity, ISoftDeletable
{
    public string Title { get; set; } = "";

    /// <summary>Plain text / newline-separated. No HTML — the clients render it
    /// into a Text node, so markup would be shown literally, not interpreted.</summary>
    public string Body { get; set; } = "";

    public NoticePriority Priority { get; set; } = NoticePriority.Normal;

    /// <summary>Empty = all roles. Same text[] + containment CHECK shape as
    /// Poll.TargetRoles, and deliberately the same label set.</summary>
    public List<string> TargetRoles { get; set; } = [];

    /// <summary>Empty = all classes. Matched against Student.Class, which is a
    /// free-text column ("Class 2", "5", "Nursery") — NOT an enum. Values are
    /// stored exactly as the school types them and compared verbatim.</summary>
    public List<string> TargetClasses { get; set; } = [];

    /// <summary>NULL = visible immediately. Future-dated notices stay hidden
    /// from non-admins until this passes, so a notice can be written ahead of
    /// an event and left to appear on its own.</summary>
    public DateTime? PublishAt { get; set; }

    /// <summary>NULL = never expires. Past this, non-admins stop seeing it —
    /// the board self-cleans instead of accumulating last year's exam notices.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Sorts above everything else regardless of date.</summary>
    public bool IsPinned { get; set; }

    [JsonPropertyName("createdBy")]
    public Guid? CreatedByUserId { get; set; }
    [JsonIgnore] public User? CreatedByUser { get; set; }

    // ISoftDeletable — DELETE sets the flag, per the project-wide rule.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Live-window test used by the read paths. Kept here rather than inline in
    /// the controller so the list query, the dashboard widget and any future
    /// digest job cannot drift apart on what "currently visible" means.
    ///
    /// NOTE: this is the in-memory form. The controller expresses the same
    /// predicate in LINQ so it translates to SQL — if you change one, change
    /// both.
    /// </summary>
    public bool IsLive(DateTime nowUtc) =>
        !IsDeleted
        && (PublishAt == null || PublishAt <= nowUtc)
        && (ExpiresAt == null || ExpiresAt > nowUtc);
}
