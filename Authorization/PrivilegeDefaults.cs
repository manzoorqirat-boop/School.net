namespace QMSoft.Api.Authorization;

/// <summary>
/// Verbatim port of config/roles.js PRIVILEGES.
///
/// MUST stay in sync with src/lib/api.ts ROLE_PRIVS. The frontend uses its copy
/// only to hide UI; every call is independently re-checked here. Drift means
/// either a hidden-but-working feature or a visible-but-403 one.
///
/// Only OVERRIDES are stored in the DB (role_privileges). A school with no
/// customisation has no rows — these defaults apply. That is what `isCustomized`
/// means in GET /api/privileges.
/// </summary>
public static class PrivilegeDefaults
{
    public const string SuperAdmin   = "superadmin";
    public const string SchoolAdmin  = "school_admin";
    public const string Principal    = "principal";
    public const string Accountant   = "accountant";
    public const string Teacher      = "teacher";
    public const string Parent       = "parent";
    public const string Student      = "student";

    public static readonly IReadOnlyDictionary<string, string[]> Map =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // ── Student management ────────────────────────────────────────────
        ["student:view"]   = [Teacher, Accountant, Principal, SchoolAdmin, SuperAdmin, Parent],
        ["student:create"] = [SchoolAdmin, Principal, SuperAdmin],
        ["student:update"] = [SchoolAdmin, Principal, SuperAdmin],
        ["student:delete"] = [SchoolAdmin, SuperAdmin],

        // ── Attendance ────────────────────────────────────────────────────
        ["attendance:view"]   = [Teacher, Principal, SchoolAdmin, SuperAdmin, Parent, Student],
        ["attendance:mark"]   = [Teacher, Principal, SchoolAdmin, SuperAdmin],
        ["attendance:report"] = [Teacher, Principal, SchoolAdmin, SuperAdmin],

        // ── Teacher (staff) attendance ────────────────────────────────────
        // RESOLVED DRIFT (API-CONTRACT §0.11): server wins. api.ts is MISSING
        // ':view' entirely, so can('teacher_attendance:view') currently returns
        // false for every role — teachers can't see a page they're allowed to use.
        // Frontend ROLE_PRIVS must be patched to match these three lines.
        ["teacher_attendance:view"]   = [Teacher, Principal, SchoolAdmin, SuperAdmin],
        ["teacher_attendance:mark"]   = [Principal, SchoolAdmin, SuperAdmin],
        ["teacher_attendance:report"] = [Principal, SchoolAdmin, SuperAdmin, Accountant],

        // ── Fees ──────────────────────────────────────────────────────────
        ["fee:view"]    = [Accountant, Principal, SchoolAdmin, SuperAdmin, Parent, Student],
        ["fee:create"]  = [Accountant, SchoolAdmin, SuperAdmin],
        ["fee:manage"]  = [Accountant, SchoolAdmin, SuperAdmin],
        ["fee:collect"] = [Accountant, SchoolAdmin, SuperAdmin],
        ["fee:report"]  = [Accountant, Principal, SchoolAdmin, SuperAdmin],

        // ── Exams ─────────────────────────────────────────────────────────
        ["exam:view"]    = [Teacher, Principal, SchoolAdmin, SuperAdmin, Parent, Student],
        ["exam:create"]  = [Teacher, Principal, SchoolAdmin, SuperAdmin],
        ["exam:grade"]   = [Teacher, Principal, SchoolAdmin, SuperAdmin],
        ["exam:publish"] = [Principal, SchoolAdmin, SuperAdmin],

        // ── Timetable ─────────────────────────────────────────────────────
        ["timetable:view"]   = [Teacher, Student, Parent, Accountant, Principal, SchoolAdmin, SuperAdmin],
        ["timetable:manage"] = [Principal, SchoolAdmin, SuperAdmin],

        // ── Teachers / Payroll ────────────────────────────────────────────
        ["teacher:view"]    = [Principal, SchoolAdmin, SuperAdmin, Accountant],
        ["teacher:manage"]  = [SchoolAdmin, SuperAdmin],
        ["payroll:view"]    = [Teacher, Accountant, Principal, SchoolAdmin, SuperAdmin],
        ["payroll:manage"]  = [Accountant, SchoolAdmin, SuperAdmin],
        ["payroll:process"] = [Accountant, SchoolAdmin, SuperAdmin],

        // ── Notifications ─────────────────────────────────────────────────
        ["notify:send"] = [Teacher, Principal, SchoolAdmin, SuperAdmin],

        // ── School / Tenant ───────────────────────────────────────────────
        ["school:view"]     = [SchoolAdmin, SuperAdmin, Principal],
        ["school:manage"]   = [SuperAdmin],
        ["school:settings"] = [SchoolAdmin, SuperAdmin],

        // ── Users ─────────────────────────────────────────────────────────
        ["user:view"]   = [Principal, SchoolAdmin, SuperAdmin],
        ["user:manage"] = [SchoolAdmin, SuperAdmin],

        // ── Audit ─────────────────────────────────────────────────────────
        ["audit:view"] = [SchoolAdmin, SuperAdmin],
    };

    public static bool Exists(string privilege) => Map.ContainsKey(privilege);
}
