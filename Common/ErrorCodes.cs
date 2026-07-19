namespace QMSoft.Api.Common;

/// <summary>
/// Machine-readable error codes. These are CONTRACT — the frontend switches on
/// them (src/lib/api.ts). Changing a string here silently breaks the client.
///
/// The TokenExpired / InvalidToken split is the load-bearing one:
///   TOKEN_EXPIRED  → api.ts silently refreshes + retries once
///   INVALID_TOKEN  → api.ts clears session and redirects to '/'
///   TOKEN_REVOKED  → same as INVALID_TOKEN
/// Return the wrong one and users bounce to login every 15 minutes.
/// </summary>
public static class ErrorCodes
{
    // ── Auth ──────────────────────────────────────────────────────────────
    public const string NoToken        = "NO_TOKEN";         // 401
    public const string InvalidToken   = "INVALID_TOKEN";    // 401 → hard logout
    public const string TokenExpired   = "TOKEN_EXPIRED";    // 401 → silent refresh
    public const string TokenRevoked   = "TOKEN_REVOKED";    // 401 → hard logout
    public const string NoRefreshToken = "NO_REFRESH_TOKEN"; // 400
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN"; // 401
    public const string MissingCredentials  = "MISSING_CREDENTIALS";   // 400
    public const string InvalidCredentials  = "INVALID_CREDENTIALS";   // 401
    public const string NotAuthenticated    = "NOT_AUTHENTICATED";     // 401
    public const string UserNotFound        = "USER_NOT_FOUND";        // 401

    // ── Password policy (utils/passwordPolicy.js) ─────────────────────────
    public const string PwMissing           = "PW_MISSING";
    public const string PwTooShort          = "PW_TOO_SHORT";
    public const string PwTooLong           = "PW_TOO_LONG";
    public const string PwNoLower           = "PW_NO_LOWER";
    public const string PwNoUpper           = "PW_NO_UPPER";
    public const string PwNoDigit           = "PW_NO_DIGIT";
    public const string PwNoSpecial         = "PW_NO_SPECIAL";
    public const string PwWeak              = "PW_WEAK";
    public const string PwContainsUsername  = "PW_CONTAINS_USERNAME";
    public const string PwSame              = "PW_SAME";

    // ── Validation / data ─────────────────────────────────────────────────
    public const string ValidationError = "VALIDATION_ERROR"; // 400 + details[]
    public const string DuplicateError  = "DUPLICATE_ERROR";  // 409 + field
    public const string InvalidId       = "INVALID_ID";       // 400
    public const string NotFound        = "NOT_FOUND";        // 404 + path, method

    // ── Plan gating (planGuard.js) ────────────────────────────────────────
    public const string NoSchool            = "NO_SCHOOL";             // 403
    public const string PlanExpired         = "PLAN_EXPIRED";          // 402
    public const string PlanInsufficient    = "PLAN_INSUFFICIENT";     // 402
    public const string StudentLimitReached = "STUDENT_LIMIT_REACHED"; // 402

    // ── Authorization ─────────────────────────────────────────────────────
    public const string Forbidden = "FORBIDDEN"; // 403

    // ── Razorpay (services/razorpay.js) ─────────────────────────────────────
    public const string RazorpayNotConfigured = "RAZORPAY_NOT_CONFIGURED"; // 400
    public const string RazorpayOrderFailed   = "RAZORPAY_ORDER_FAILED";   // upstream status
    public const string RazorpayPayoutFailed  = "RAZORPAY_PAYOUT_FAILED";  // upstream status

    // ── Fallback ──────────────────────────────────────────────────────────
    public const string InternalError = "INTERNAL_ERROR"; // 500
}
