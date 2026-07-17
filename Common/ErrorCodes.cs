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

    // ── Fallback ──────────────────────────────────────────────────────────
    public const string InternalError = "INTERNAL_ERROR"; // 500
}
