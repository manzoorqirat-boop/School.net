using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<PollStatus>))]
public enum PollStatus
{
    [EnumMember(Value = "draft")]  Draft,
    [EnumMember(Value = "active")] Active,
    [EnumMember(Value = "closed")] Closed,
}

[JsonConverter(typeof(EnumMemberJsonConverter<PollCategory>))]
public enum PollCategory
{
    [EnumMember(Value = "satisfaction")] Satisfaction,
    [EnumMember(Value = "event")]        Event,
    [EnumMember(Value = "canteen")]      Canteen,
    [EnumMember(Value = "general")]      General,
}

public class Poll : TenantEntity
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public PollCategory Category { get; set; } = PollCategory.General;

    /// <summary>
    /// Who can vote. text[] with a CHECK (superadmin/parent-of-nothing can't be
    /// targeted — the Mongo enum excludes superadmin too). Default matches the
    /// Mongo schema: ['parent','teacher'].
    /// </summary>
    public List<string> TargetRoles { get; set; } = ["parent", "teacher"];

    /// <summary>{_id:true} → child table, _id serialized — PollVote.answers FK
    /// questions and options BY ID.</summary>
    public ICollection<PollQuestion> Questions { get; set; } = [];

    public PollStatus Status { get; set; } = PollStatus.Draft;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public bool ShowResultsBeforeClose { get; set; } = true;

    /// <summary>Hide voter names in results (votes still keyed to userId for
    /// the one-vote-per-user constraint — anonymity is display-level only,
    /// exactly as in Node).</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>Mongo field is `createdBy` — wire name pinned.</summary>
    [JsonPropertyName("createdBy")]
    public Guid? CreatedByUserId { get; set; }
    [JsonIgnore] public User? CreatedByUser { get; set; }

    /// <summary>The Mongoose array validators (≥1 question, ≥2 options each) —
    /// call from the write path.</summary>
    public void Validate()
    {
        if (Questions.Count < 1)
            throw new AppException("A poll needs at least one question",
                400, ErrorCodes.ValidationError);

        foreach (var q in Questions)
            if (q.Options.Count < 2)
                throw new AppException("Each question needs at least two options",
                    400, ErrorCodes.ValidationError);
    }
}

public class PollQuestion : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid PollId { get; set; }
    [JsonIgnore] public Poll Poll { get; set; } = null!;

    public string Text { get; set; } = "";

    public ICollection<PollOption> Options { get; set; } = [];
}

public class PollOption : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid QuestionId { get; set; }
    [JsonIgnore] public PollQuestion Question { get; set; } = null!;

    public string Text { get; set; } = "";
}

/// <summary>
/// One user's submission. Mongo had only submittedAt (no createdAt/updatedAt)
/// — kept faithful: plain IEntity+ITenantScoped, not TenantEntity.
/// </summary>
public class PollVote : IEntity, ITenantScoped
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonPropertyName("schoolId")]
    public Guid SchoolId { get; set; }

    public Guid PollId { get; set; }
    [JsonIgnore] public Poll Poll { get; set; } = null!;

    public Guid UserId { get; set; }
    [JsonIgnore] public User User { get; set; } = null!;

    // Denormalised for quick display (results table).
    public string? UserName { get; set; }
    public string? UserRole { get; set; }

    /// <summary>{_id:false} but a CHILD TABLE — each answer FKs a question and
    /// an option, and result tallies GROUP BY option_id (SCHEMA-MAP §1.1
    /// exception list). PK internal.</summary>
    public ICollection<PollVoteAnswer> Answers { get; set; } = [];

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public class PollVoteAnswer
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid VoteId { get; set; }
    [JsonIgnore] public PollVote Vote { get; set; } = null!;

    public Guid QuestionId { get; set; }
    public Guid OptionId { get; set; }
}
