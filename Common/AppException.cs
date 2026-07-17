using System.Net;
using System.Text.Json.Serialization;

namespace QMSoft.Api.Common;

/// <summary>
/// Port of middleware/errorHandler.js `AppError`.
/// Throw this for expected failures; ExceptionHandlingMiddleware renders it.
/// </summary>
public class AppException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    /// <summary>Extra top-level fields merged into the JSON body (e.g. `field`,
    /// `currentPlan`, `expiredAt`). Matches how errorHandler.js splices extras in.</summary>
    public IReadOnlyDictionary<string, object?>? Extras { get; }

    public AppException(
        string message,
        int statusCode = 500,
        string code = ErrorCodes.InternalError,
        IReadOnlyDictionary<string, object?>? extras = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Extras = extras;
    }

    // ── Factories for the common cases ────────────────────────────────────

    public static AppException NotFound(string what = "Resource not found") =>
        new(what, (int)HttpStatusCode.NotFound, ErrorCodes.NotFound);

    public static AppException Forbidden(string message = "Forbidden") =>
        new(message, (int)HttpStatusCode.Forbidden, ErrorCodes.Forbidden);

    public static AppException Duplicate(string field) =>
        new($"{field} already exists", (int)HttpStatusCode.Conflict, ErrorCodes.DuplicateError,
            new Dictionary<string, object?> { ["field"] = field });

    public static AppException InvalidId(string message = "Invalid ID format") =>
        new(message, (int)HttpStatusCode.BadRequest, ErrorCodes.InvalidId);

    // ── Plan gating (402s) ────────────────────────────────────────────────

    public static AppException PlanExpired(string plan, DateTime expiredAt) =>
        new("Your subscription has expired. Please renew to continue.",
            402, ErrorCodes.PlanExpired,
            new Dictionary<string, object?> { ["plan"] = plan, ["expiredAt"] = expiredAt });

    public static AppException PlanInsufficient(string currentPlan, string requiredPlan) =>
        new($"This feature requires the '{requiredPlan}' plan or higher. Your current plan is '{currentPlan}'.",
            402, ErrorCodes.PlanInsufficient,
            new Dictionary<string, object?>
            {
                ["currentPlan"] = currentPlan,
                ["requiredPlan"] = requiredPlan,
            });

    public static AppException StudentLimitReached(string currentPlan, int limit, int current) =>
        new($"Your '{currentPlan}' plan supports up to {limit} students. Please upgrade to add more.",
            402, ErrorCodes.StudentLimitReached,
            new Dictionary<string, object?>
            {
                ["currentPlan"] = currentPlan,
                ["limit"] = limit,
                ["current"] = current,
            });
}

/// <summary>
/// One Joi-style validation failure.
/// api.ts reads `details[0].message` and appends it to the toast, so `message`
/// must be human-readable, not a code.
/// </summary>
public sealed class ValidationDetail
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}
