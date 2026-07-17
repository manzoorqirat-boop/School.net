using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

[JsonConverter(typeof(EnumMemberJsonConverter<AttendanceStatus>))]
public enum AttendanceStatus
{
    [EnumMember(Value = "present")] Present,
    [EnumMember(Value = "absent")]  Absent,
    [EnumMember(Value = "late")]    Late,
    [EnumMember(Value = "leave")]   Leave,
    [EnumMember(Value = "holiday")] Holiday,
}

[JsonConverter(typeof(EnumMemberJsonConverter<AttendanceMode>))]
public enum AttendanceMode
{
    [EnumMember(Value = "daily")]  Daily,
    [EnumMember(Value = "period")] Period,
}

/// <summary>
/// 7 statuses, NOT the student 5 — half_day, unpaid_leave and on_duty are
/// staff-specific, and three of the seven carry payroll weight.
/// </summary>
[JsonConverter(typeof(EnumMemberJsonConverter<TeacherAttendanceStatus>))]
public enum TeacherAttendanceStatus
{
    [EnumMember(Value = "present")]      Present,
    [EnumMember(Value = "absent")]       Absent,
    [EnumMember(Value = "half_day")]     HalfDay,
    [EnumMember(Value = "leave")]        Leave,        // approved PAID leave
    [EnumMember(Value = "unpaid_leave")] UnpaidLeave,
    [EnumMember(Value = "on_duty")]      OnDuty,       // training, exam duty
    [EnumMember(Value = "holiday")]      Holiday,
}

/// <summary>Student daily/period attendance.</summary>
public class Attendance : TenantEntity
{
    public Guid StudentId { get; set; }
    [JsonIgnore] public Student Student { get; set; } = null!;

    public string AcademicYear { get; set; } = "";
    public string Class { get; set; } = "";
    public string Section { get; set; } = "";

    /// <summary>
    /// DateOnly deletes the Mongo setUTCHours(0,0,0,0) normalisation hack — the
    /// pre('save') existed only because Mongo has no date type. IST is UTC+5:30;
    /// a timestamptz "date" written at 23:00 IST lands on the previous UTC day.
    /// </summary>
    public DateOnly Date { get; set; }

    public AttendanceMode Mode { get; set; }

    /// <summary>1..12 in period mode, null in daily mode. CHECK-constrained.</summary>
    public short? Period { get; set; }

    public string? Subject { get; set; }

    public AttendanceStatus Status { get; set; }

    public string? Remarks { get; set; }

    /// <summary>When 'late' — the actual arrival time. Full timestamp, not date.</summary>
    public DateTime? ArrivedAt { get; set; }

    /// <summary>When 'leave'.</summary>
    public string? LeaveReason { get; set; }

    public Guid? MarkedByUserId { get; set; }
    [JsonIgnore] public User? MarkedByUser { get; set; }

    /// <summary>Snapshot for rendering without a join — keep (SCHEMA-MAP §11).</summary>
    public string? MarkedByName { get; set; }
}

/// <summary>
/// CONTRACT + payroll business rules for attendance, in ONE place.
///
/// contracts.ts pins the summary shape and the UI does NOT recompute:
///   summary.total      = working days, EXCLUDES holiday
///   summary.percentage = (present + late) / total, ONE decimal
///
/// 'late' counts as present. These rules must not drift into ad-hoc LINQ.
/// </summary>
public static class AttendanceRules
{
    public static bool CountsAsWorkingDay(AttendanceStatus s) => s != AttendanceStatus.Holiday;

    public static bool CountsAsPresent(AttendanceStatus s) =>
        s is AttendanceStatus.Present or AttendanceStatus.Late;

    /// <summary>Percentage to ONE decimal, matching the Node round(x*10)/10.</summary>
    public static decimal Percentage(int present, int late, int workingTotal) =>
        workingTotal <= 0
            ? 0m
            : Math.Round((present + late) * 100m / workingTotal, 1,
                         MidpointRounding.AwayFromZero);
}

/// <summary>
/// Staff attendance register. Deliberately separate from Attendance — the Mongo
/// model comment is explicit: no class/section/period context, check-in/out
/// times, and it FEEDS PAYROLL via unpaid-day weights.
/// </summary>
public class TeacherAttendance : TenantEntity
{
    /// <summary>A User with role='teacher' — consistent with payroll keying.</summary>
    public Guid TeacherId { get; set; }
    [JsonIgnore] public User Teacher { get; set; } = null!;

    public string AcademicYear { get; set; } = "";

    public DateOnly Date { get; set; }

    public TeacherAttendanceStatus Status { get; set; }

    /// <summary>Optional; relevant for present/half_day. Full timestamps.</summary>
    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }

    /// <summary>Where the teacher was, e.g. "CBSE training, Jaipur".</summary>
    public string? OnDutyNote { get; set; }

    public string? Remarks { get; set; }

    /// <summary>Snapshot for rendering without a join.</summary>
    public string? TeacherName { get; set; }

    public Guid? MarkedByUserId { get; set; }
    [JsonIgnore] public User? MarkedByUser { get; set; }
    public string? MarkedByName { get; set; }
}

/// <summary>
/// UNPAID_WEIGHT — verbatim from TeacherAttendance.js.
///
/// payrollController.unpaidLeaveDaysFor() sums these to compute the salary
/// deduction for a month. One source of truth shared by payroll AND reports,
/// exactly as the Node model kept it. When Payroll is ported (Phase 1 cont.),
/// it must read THIS — reimplementing the weights there recreates the drift
/// this table exists to prevent.
///
/// half_day = 0.5 is why Leave.days and deduction math are numeric(5,1)/decimal,
/// never int.
/// </summary>
public static class TeacherAttendanceRules
{
    public static readonly IReadOnlyDictionary<TeacherAttendanceStatus, decimal> UnpaidWeight =
        new Dictionary<TeacherAttendanceStatus, decimal>
        {
            [TeacherAttendanceStatus.Present]     = 0m,
            [TeacherAttendanceStatus.Absent]      = 1m,
            [TeacherAttendanceStatus.HalfDay]     = 0.5m,
            [TeacherAttendanceStatus.Leave]       = 0m,    // paid leave
            [TeacherAttendanceStatus.UnpaidLeave] = 1m,
            [TeacherAttendanceStatus.OnDuty]      = 0m,
            [TeacherAttendanceStatus.Holiday]     = 0m,
        };

    public static decimal UnpaidDays(IEnumerable<TeacherAttendanceStatus> monthStatuses) =>
        monthStatuses.Sum(s => UnpaidWeight[s]);
}

/// <summary>
/// Teacher↔class assignment. isPrimary=true is the homeroom teacher who marks
/// daily attendance; subject-specific rows exist for period-wise marking.
/// </summary>
public class ClassTeacher : TenantEntity
{
    public Guid TeacherUserId { get; set; }
    [JsonIgnore] public User TeacherUser { get; set; } = null!;

    public string AcademicYear { get; set; } = "";
    public string Class { get; set; } = "";
    public string Section { get; set; } = "";

    /// <summary>Null = general/homeroom assignment. In the UNIQUE key — see config.</summary>
    public string? Subject { get; set; }

    public bool IsPrimary { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class Subject : TenantEntity
{
    public string Name { get; set; } = "";

    /// <summary>'MATH', '041' (CBSE-style). Uppercased on write — see config.</summary>
    public string? Code { get; set; }

    public string Class { get; set; } = "";
    public string AcademicYear { get; set; } = "";

    /// <summary>Art/Music/PE — graded only (A/B/C), not counted toward % or rank.</summary>
    public bool IsCoScholastic { get; set; }

    public decimal DefaultMaxMarks { get; set; } = 100m;

    /// <summary>Report-card sort order.</summary>
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
