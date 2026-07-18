using System.Text.RegularExpressions;

namespace QMSoft.Api.Common;

/// <summary>
/// Port of utils/passwordPolicy.js, verbatim rule-for-rule:
///   • &gt;= 8 characters, &lt;= 128
///   • &gt;= 1 lowercase, &gt;= 1 uppercase, &gt;= 1 digit, &gt;= 1 special character
///   • not in the small in-memory weak-password blocklist
///   • must not contain the username
///
/// Deliberately no length &gt;= 12 or breach-database (HIBP) check — same
/// reasoning as the Node original: those punish parents on phones and add a
/// network call to every password change.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private static readonly HashSet<string> WeakPasswords = new(StringComparer.Ordinal)
    {
        "password", "password1", "password123", "password@123",
        "admin@123", "admin123", "qmsoft@123",
        "12345678", "11111111", "abc12345", "qwerty12",
        "welcome1", "letmein1", "iloveyou",
    };

    private static readonly Regex Lower   = new("[a-z]", RegexOptions.Compiled);
    private static readonly Regex Upper   = new("[A-Z]", RegexOptions.Compiled);
    private static readonly Regex Digit   = new(@"\d", RegexOptions.Compiled);
    private static readonly Regex Special = new("[^A-Za-z0-9]", RegexOptions.Compiled);

    public readonly record struct Result(bool Ok, string? Error, string? Code)
    {
        public static Result Success() => new(true, null, null);
        public static Result Fail(string error, string code) => new(false, error, code);
    }

    public static Result Validate(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password))
            return Result.Fail("Password is required", ErrorCodes.PwMissing);

        if (password.Length < MinLength)
            return Result.Fail($"Password must be at least {MinLength} characters", ErrorCodes.PwTooShort);

        if (password.Length > MaxLength)
            return Result.Fail("Password is too long (max 128 characters)", ErrorCodes.PwTooLong);

        if (!Lower.IsMatch(password))
            return Result.Fail("Password must contain a lowercase letter", ErrorCodes.PwNoLower);

        if (!Upper.IsMatch(password))
            return Result.Fail("Password must contain an uppercase letter", ErrorCodes.PwNoUpper);

        if (!Digit.IsMatch(password))
            return Result.Fail("Password must contain a number", ErrorCodes.PwNoDigit);

        if (!Special.IsMatch(password))
            return Result.Fail("Password must contain a special character (e.g. @ # $ !)", ErrorCodes.PwNoSpecial);

        if (WeakPasswords.Contains(password.ToLowerInvariant()))
            return Result.Fail("This password is too common — please choose a stronger one", ErrorCodes.PwWeak);

        if (!string.IsNullOrEmpty(username) &&
            password.Contains(username, StringComparison.OrdinalIgnoreCase))
            return Result.Fail("Password must not contain your username", ErrorCodes.PwContainsUsername);

        return Result.Success();
    }
}
