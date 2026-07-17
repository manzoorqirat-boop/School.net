using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using Npgsql;

namespace QMSoft.Api.Infrastructure;

/// <summary>
/// Maps CLR enum members to Postgres enum labels using [EnumMember], keeping ONE
/// source of truth (Enums.cs) for the JSON string, the DB label and the DDL.
///
/// Npgsql's default translator snake_cases member names — the same mechanical
/// rule that yields 'super_admin' for SuperAdmin, 'g_e_n' for GEN and 'hindu'
/// for Hindu. All three are wrong here (verified: 8 of 33 values diverge). The
/// failure is silent on write and throws far from the cause on read.
///
/// ── Why one instance PER ENUM ─────────────────────────────────────────────
/// INpgsqlNameTranslator is string→string, with no MemberInfo overload
/// (verified against the Npgsql v8.0.5 source, not assumed):
///
///     string TranslateTypeName(string clrName);
///     string TranslateMemberName(string clrName);
///
/// So the translator cannot see which enum a member came from. A single shared
/// map keyed by member name COLLIDES in this schema — verified:
///
///     Gender.Other        → "other"
///     TransportMode.Other → "other"
///     Religion.Other      → "Other"   ← same member name, different label
///
/// One instance per enum sidesteps it: each map holds a single enum's members,
/// so 'Other' is unambiguous within it. Use <see cref="For{T}"/>.
/// </summary>
public sealed class EnumMemberNameTranslator : INpgsqlNameTranslator
{
    private static readonly ConcurrentDictionary<Type, EnumMemberNameTranslator> Cache = new();

    private readonly Dictionary<string, string> _labels;
    private readonly string _enumName;

    private EnumMemberNameTranslator(Type enumType)
    {
        _enumName = enumType.Name;
        _labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var f in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = f.GetCustomAttribute<EnumMemberAttribute>();

            if (attr?.Value is null)
            {
                throw new InvalidOperationException(
                    $"{enumType.Name}.{f.Name} is missing [EnumMember(Value = \"...\")]. " +
                    "Postgres enum labels are contract — declare them in Enums.cs.");
            }

            _labels[f.Name] = attr.Value;
        }
    }

    /// <summary>
    /// The translator for a single enum. Cached — Npgsql holds the instance for
    /// the life of the data source.
    /// </summary>
    public static EnumMemberNameTranslator For<T>() where T : struct, Enum =>
        Cache.GetOrAdd(typeof(T), t => new EnumMemberNameTranslator(t));

    public string TranslateTypeName(string clrName) => clrName;

    public string TranslateMemberName(string clrName) =>
        _labels.TryGetValue(clrName, out var label)
            ? label
            // Loud, not silent: the snake_case fallback would send 'g_e_n' to
            // Postgres and fail somewhere unrelated.
            : throw new InvalidOperationException(
                $"'{clrName}' is not a member of {_enumName}, or is missing " +
                "[EnumMember]. See Enums.cs.");
}
