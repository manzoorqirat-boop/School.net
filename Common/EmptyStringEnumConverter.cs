using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QMSoft.Api.Common;

/// <summary>
/// Single source of truth for enum wire values, read from [EnumMember].
///
/// Mechanical PascalCase→snake_case DOES NOT WORK for this schema — verified,
/// 8 of 33 values diverge ('superadmin' not 'super_admin'; 'GEN' not 'g_e_n';
/// 'Hindu' not 'hindu'). Every enum member declares its literal explicitly.
///
/// Throws at first use if a member is missing [EnumMember] — a loud failure at
/// boot beats a silent 'g_e_n' reaching the frontend.
/// </summary>
internal static class EnumWire<T> where T : struct, Enum
{
    private static readonly Dictionary<T, string> ToWireMap = Build();
    private static readonly Dictionary<string, T> FromWireMap =
        ToWireMap.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    private static Dictionary<T, string> Build()
    {
        var map = new Dictionary<T, string>();

        foreach (var f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = f.GetCustomAttribute<EnumMemberAttribute>();

            if (attr?.Value is null)
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name}.{f.Name} is missing [EnumMember(Value = \"...\")]. " +
                    "Wire values are contract and must be explicit — see Enums.cs.");
            }

            map[(T)f.GetValue(null)!] = attr.Value;
        }

        return map;
    }

    public static string ToWire(T v) => ToWireMap[v];

    public static bool TryParse(string s, out T v) => FromWireMap.TryGetValue(s, out v);

    public static IReadOnlyCollection<string> Values => FromWireMap.Keys;
}

/// <summary>Non-nullable enums — exact [EnumMember] round-trip, case-sensitive.</summary>
public sealed class EnumMemberJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
    {
        var s = reader.GetString();

        if (string.IsNullOrEmpty(s))
            throw new JsonException($"Expected a value for {typeof(T).Name}.");

        if (EnumWire<T>.TryParse(s, out var v)) return v;

        throw new JsonException(
            $"'{s}' is not a valid {typeof(T).Name}. " +
            $"Expected one of: {string.Join(", ", EnumWire<T>.Values)}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions _)
        => writer.WriteStringValue(EnumWire<T>.ToWire(value));
}

/// <summary>
/// Student.gender / category / religion / transportMode carry '' in their Mongo
/// enums, with the comment: "added so front-end default of "" doesn't cause
/// Mongoose rejection". That is a frontend default leaking into the data model.
///
/// Postgres stores NULL. This converter keeps the leak at the edge:
///   read:  "" → null
///   write: null → ""
///
/// The WRITE side is not optional. StudentForm.tsx binds a &lt;select&gt; to these
/// fields; emitting null gives an unselected dropdown instead of the "—"
/// placeholder option. Symmetric, so the frontend needs no change.
///
/// Applies to POST /api/students AND /bulk-import — which is exactly why this
/// belongs at the JSON boundary rather than in the form.
/// </summary>
public sealed class EmptyStringToNullEnumConverter<T> : JsonConverter<T?>
    where T : struct, Enum
{
    public override T? Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string for {typeof(T).Name}, got {reader.TokenType}.");

        var s = reader.GetString();
        if (string.IsNullOrEmpty(s)) return null;

        if (EnumWire<T>.TryParse(s, out var v)) return v;

        throw new JsonException(
            $"'{s}' is not a valid {typeof(T).Name}. " +
            $"Expected one of: {string.Join(", ", EnumWire<T>.Values)}, or \"\".");
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions _)
    {
        if (value is null) writer.WriteStringValue("");   // NOT WriteNullValue()
        else writer.WriteStringValue(EnumWire<T>.ToWire(value.Value));
    }
}
