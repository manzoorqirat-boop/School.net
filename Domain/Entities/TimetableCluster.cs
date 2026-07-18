using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<TimetableStatus>))]
public enum TimetableStatus
{
    [EnumMember(Value = "draft")]    Draft,
    [EnumMember(Value = "active")]   Active,
    [EnumMember(Value = "archived")] Archived,
}

[JsonConverter(typeof(EnumMemberJsonConverter<VariationType>))]
public enum VariationType
{
    [EnumMember(Value = "substitute_teacher")] SubstituteTeacher,
    [EnumMember(Value = "cancelled")]          Cancelled,
    [EnumMember(Value = "rescheduled")]        Rescheduled,
    [EnumMember(Value = "guest_lecture")]      GuestLecture,
    [EnumMember(Value = "custom")]             Custom,
}

// ─────────────────────────────────────────────────────────────────────────────
// dayOfWeek is an INTEGER 0=Sun..6=Sat everywhere in this cluster.
// contracts.ts warns, verbatim: "(NOT a `day` string!)" — a bug they already
// ate once. `short` (not an enum, not System.DayOfWeek) guarantees a JSON
// NUMBER on the wire; an enum would tempt someone into a string converter.
// Times ("09:00") stay strings — they feed <input type="time"> directly.
// ─────────────────────────────────────────────────────────────────────────────

public class TimeSlot : TenantEntity
{
    /// <summary>'Period 1', 'Assembly', 'Lunch'.</summary>
    public string Name { get; set; } = "";

    /// <summary>1, 2, 3… — ordering + the join key from timetable entries.</summary>
    public int SlotNumber { get; set; }

    public string DefaultStartTime { get; set; } = "";
    public string DefaultEndTime { get; set; } = "";

    /// <summary>
    /// Per-day overrides (Sat periods are often shorter). Mongo {_id:false},
    /// tiny, never queried individually → JSONB.
    /// </summary>
    public List<SlotDayTime> DayTimes { get; set; } = [];

    /// <summary>Lab spanning periods 3–4: this slot chains to the next.</summary>
    public bool SpansMultiplePeriods { get; set; }
    public int? NextSlotNumber { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Domain ────────────────────────────────────────────────────────────

    /// <summary>
    /// Port of the pre('save') duration computation — call before SaveChanges
    /// on any write path (the hook's updateOne blind spot, again).
    /// Node did no end&gt;start validation; a negative duration passes through
    /// unchanged. Preserved — the UI treats duration as display-only.
    /// </summary>
    public void ComputeDurations()
    {
        foreach (var dt in DayTimes)
            dt.Duration = Minutes(dt.EndTime) - Minutes(dt.StartTime);
    }

    /// <summary>Port of getTimeForDay(dayOfWeek): day-specific override, else defaults.</summary>
    public (string StartTime, string EndTime, int Duration) GetTimeForDay(short dayOfWeek)
    {
        var d = DayTimes.FirstOrDefault(x => x.DayOfWeek == dayOfWeek);
        if (d is not null)
            return (d.StartTime, d.EndTime, d.Duration ?? Minutes(d.EndTime) - Minutes(d.StartTime));

        return (DefaultStartTime, DefaultEndTime,
                Minutes(DefaultEndTime) - Minutes(DefaultStartTime));
    }

    /// <summary>"09:45" → 585. Node's split(':').map(Number) equivalent.</summary>
    internal static int Minutes(string hhmm)
    {
        var parts = hhmm.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var h)
            || !int.TryParse(parts[1], out var m))
        {
            throw new AppException($"Invalid time '{hhmm}' — expected HH:mm",
                400, ErrorCodes.ValidationError);
        }
        return h * 60 + m;
    }
}

/// <summary>JSONB element — no _id, no table.</summary>
public sealed class SlotDayTime
{
    public short DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";

    /// <summary>Minutes; recomputed by ComputeDurations(), stored for display.</summary>
    public int? Duration { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class Timetable : TenantEntity
{
    public string Class { get; set; } = "";
    public string Section { get; set; } = "";
    public string AcademicYear { get; set; } = "";

    /// <summary>Effective window; open-ended when ToDate is null.</summary>
    public DateOnly FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>'Term 1', 'Annual' — organisational only.</summary>
    public string? Term { get; set; }

    /// <summary>{_id:true} → child table, _id serialized: DELETE
    /// /:id/entries/:entryId addresses rows by this id.</summary>
    public ICollection<TimetableEntry> Entries { get; set; } = [];

    public TimetableStatus Status { get; set; } = TimetableStatus.Draft;
}

/// <summary>"Mon Period 1: Teacher X teaches Math to 5-A".</summary>
public class TimetableEntry : IEntity
{
    [JsonPropertyName("_id")]
    public Guid Id { get; set; }

    [JsonIgnore] public Guid TimetableId { get; set; }
    [JsonIgnore] public Timetable Timetable { get; set; } = null!;

    /// <summary>0=Sun..6=Sat. INTEGER on the wire — contracts.ts insists.</summary>
    public short DayOfWeek { get; set; }

    /// <summary>Joins TimeSlot.SlotNumber (by number, not FK — slots are
    /// per-school config that timetables reference loosely; a slot edit must
    /// not cascade into historical timetables).</summary>
    public int SlotNumber { get; set; }

    public Guid TeacherId { get; set; }
    [JsonIgnore] public User Teacher { get; set; } = null!;
    public string? TeacherName { get; set; }        // snapshot

    public Guid? SubjectId { get; set; }
    [JsonIgnore] public Subject? Subject { get; set; }
    public string? SubjectName { get; set; }

    public string? Room { get; set; }
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Per-DATE override of the base grid: substitution, cancellation, etc.</summary>
public class TimetableVariation : TenantEntity
{
    public Guid TimetableId { get; set; }
    [JsonIgnore] public Timetable Timetable { get; set; } = null!;

    private DateOnly _date;

    /// <summary>
    /// Setting the date also snapshots DayOfWeek — port of the pre('save')
    /// `this.dayOfWeek = new Date(this.date).getDay()`. Node's getDay() on a
    /// UTC-midnight date equals the calendar weekday; DateOnly.DayOfWeek is
    /// the same calendar computation, and (int)System.DayOfWeek is 0=Sunday —
    /// identical numbering by construction.
    /// </summary>
    public DateOnly Date
    {
        get => _date;
        set
        {
            _date = value;
            DayOfWeek = (short)value.DayOfWeek;
        }
    }

    /// <summary>Snapshot, derived from Date. 0=Sun..6=Sat.</summary>
    public short DayOfWeek { get; set; }

    public int SlotNumber { get; set; }

    public VariationType Type { get; set; } = VariationType.SubstituteTeacher;

    // New values, when substituting/custom.
    public Guid? TeacherId { get; set; }
    [JsonIgnore] public User? Teacher { get; set; }
    public string? TeacherName { get; set; }

    public Guid? SubjectId { get; set; }
    [JsonIgnore] public Subject? Subject { get; set; }
    public string? SubjectName { get; set; }

    public string? Room { get; set; }
    public string? Notes { get; set; }

    /// <summary>'Teacher sick', 'Special class'.</summary>
    public string? Reason { get; set; }

    public Guid? CreatedByUserId { get; set; }
    [JsonIgnore] public User? CreatedByUser { get; set; }
}
