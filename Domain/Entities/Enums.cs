using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using QMSoft.Api.Common;

namespace QMSoft.Api.Domain.Entities;

// Wire values come straight from the Mongo string enums and are CONTRACT —
// api.ts and the page components compare against these literals.
//
// Auto-derived casing DOES NOT WORK here. Verified by simulation, 8 of 33 values
// diverge from any mechanical PascalCase→snake_case rule:
//
//   SuperAdmin → 'super_admin'  but Mongo/api.ts say 'superadmin'
//   GEN        → 'g_e_n'        but Mongo says 'GEN'   (same for OBC/SC/ST/EWS)
//   Hindu      → 'hindu'        but Mongo says 'Hindu' (same for every religion)
//
// So every member carries an explicit [EnumMember] and EnumMemberJsonConverter
// reads it. No inference, no surprises.
//
// A wrong UserRole value is the worst failure mode in the app: the JWT role
// claim would never match PrivilegeDefaults, so every privileged endpoint would
// 403 with no obvious cause.

[JsonConverter(typeof(EnumMemberJsonConverter<UserRole>))]
public enum UserRole
{
    [EnumMember(Value = "superadmin")]   SuperAdmin,
    [EnumMember(Value = "school_admin")] SchoolAdmin,
    [EnumMember(Value = "principal")]    Principal,
    [EnumMember(Value = "accountant")]   Accountant,
    [EnumMember(Value = "teacher")]      Teacher,
    [EnumMember(Value = "parent")]       Parent,
    [EnumMember(Value = "student")]      Student,
}

[JsonConverter(typeof(EnumMemberJsonConverter<SchoolType>))]
public enum SchoolType
{
    [EnumMember(Value = "k12")]      K12,
    [EnumMember(Value = "coaching")] Coaching,
    [EnumMember(Value = "college")]  College,
    [EnumMember(Value = "other")]    Other,
}

[JsonConverter(typeof(EnumMemberJsonConverter<SchoolPlan>))]
public enum SchoolPlan
{
    // Order is load-bearing: planGuard.js PLAN_RANK is trial<basic<pro<enterprise.
    // Declaring them in rank order makes `plan >= SchoolPlan.Pro` valid and
    // deletes the lookup dictionary.
    [EnumMember(Value = "trial")]      Trial = 0,
    [EnumMember(Value = "basic")]      Basic = 1,
    [EnumMember(Value = "pro")]        Pro = 2,
    [EnumMember(Value = "enterprise")] Enterprise = 3,
}

[JsonConverter(typeof(EnumMemberJsonConverter<StudentStatus>))]
public enum StudentStatus
{
    [EnumMember(Value = "active")]      Active,
    [EnumMember(Value = "inactive")]    Inactive,
    [EnumMember(Value = "transferred")] Transferred,
    [EnumMember(Value = "graduated")]   Graduated,
}

// ── Nullable enums: '' on the wire, NULL in the DB ────────────────────────
// Student.gender/category/religion/transportMode carry '' in the Mongo enum with
// the comment "added so front-end default of "" doesn't cause Mongoose
// rejection". EmptyStringToNullEnumConverter keeps that leak at the JSON edge:
// read "" → null, write null → "".
//
// No [JsonConverter] attribute on these — the property-level converter on
// Student wins, and attaching both would be ambiguous.

public enum Gender
{
    [EnumMember(Value = "male")]   Male,
    [EnumMember(Value = "female")] Female,
    [EnumMember(Value = "other")]  Other,
}

public enum StudentCategory
{
    // All-caps in Mongo. A mechanical converter emits 'g_e_n'.
    [EnumMember(Value = "GEN")] GEN,
    [EnumMember(Value = "OBC")] OBC,
    [EnumMember(Value = "SC")]  SC,
    [EnumMember(Value = "ST")]  ST,
    [EnumMember(Value = "EWS")] EWS,
}

public enum Religion
{
    // Title-case in Mongo.
    [EnumMember(Value = "Hindu")]     Hindu,
    [EnumMember(Value = "Muslim")]    Muslim,
    [EnumMember(Value = "Sikh")]      Sikh,
    [EnumMember(Value = "Christian")] Christian,
    [EnumMember(Value = "Buddhist")]  Buddhist,
    [EnumMember(Value = "Jain")]      Jain,
    [EnumMember(Value = "Other")]     Other,
}

public enum TransportMode
{
    [EnumMember(Value = "self")]       Self,
    [EnumMember(Value = "school_bus")] SchoolBus,
    [EnumMember(Value = "walk")]       Walk,
    [EnumMember(Value = "other")]      Other,
}

public enum SiblingRelation
{
    [EnumMember(Value = "brother")] Brother,
    [EnumMember(Value = "sister")]  Sister,
}
