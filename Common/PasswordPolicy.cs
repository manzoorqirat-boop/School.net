namespace QMSoft.Api.Common;

public sealed record PasswordCheck(bool Ok, string? Error = null, string? Code = null);

/// <summary>
/// Verbatim port of utils/passwordPolicy.js — used by change-password, user
/// creation and reset paths so policy stays consistent. Messages and codes are
/// CONTRACT: api.ts surfaces `error` verbatim and may branch on `code`.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLen = 8;
    private const int MaxLen = 128;

    private static readonly HashSet<string> Weak = new(StringComparer.Ordinal)
    {
        "password", "password1", "password123", "password@123",
        "admin@123", "admin123", "qmsoft@123",
        "12345678", "11111111", "abc12345", "qwerty12",
        "welcome1", "letmein1", "iloveyou",
    };

    public static PasswordCheck Validate(string? pw, string? username = null)
    {
        if (pw is null)
            return new(false, "Password is required", "PW_MISSING");
        if (pw.Length < MinLen)
            return new(false, $"Password must be at least {MinLen} characters", "PW_TOO_SHORT");
        if (pw.Length > MaxLen)
            return new(false, "Password is too long (max 128 characters)", "PW_TOO_LONG");
        if (!pw.Any(char.IsAsciiLetterLower))
            return new(false, "Password must contain a lowercase letter", "PW_NO_LOWER");
        if (!pw.Any(char.IsAsciiLetterUpper))
            return new(false, "Password must contain an uppercase letter", "PW_NO_UPPER");
        if (!pw.Any(char.IsAsciiDigit))
            return new(false, "Password must contain a number", "PW_NO_DIGIT");
        if (pw.All(char.IsAsciiLetterOrDigit))
            return new(false, "Password must contain a special character (e.g. @ # $ !)", "PW_NO_SPECIAL");
        if (Weak.Contains(pw.ToLowerInvariant()))
            return new(false, "This password is too common — please choose a stronger one", "PW_WEAK");
        if (!string.IsNullOrEmpty(username) &&
            pw.Contains(username, StringComparison.OrdinalIgnoreCase))
            return new(false, "Password must not contain your username", "PW_CONTAINS_USERNAME");

        return new(true);
    }
}
