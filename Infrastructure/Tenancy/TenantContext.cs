using System.Security.Claims;

namespace QMSoft.Api.Infrastructure.Tenancy;

public interface ITenantContext
{
    /// <summary>Active school, or null for superadmin / unauthenticated paths.</summary>
    Guid? SchoolId { get; }

    Guid? UserId { get; }
    string? Role { get; }

    /// <summary>True when the query filter should be bypassed entirely.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// True when a tenant-scoped filter should apply. False for superadmin and
    /// for unauthenticated endpoints (public share link, Razorpay webhook),
    /// which handle their own scoping explicitly.
    /// </summary>
    bool IsFilterActive { get; }

    /// <summary>Throws when a school context is required but absent (planGuard NO_SCHOOL).</summary>
    Guid RequireSchoolId();
}

/// <summary>
/// Replaces middleware/tenant.js.
///
/// The Node original did School.findById() per request and cached it in Redis
/// for 5 min to survive it. We resolve straight from JWT claims — no DB hit, no
/// cache, no invalidation problem. The School row is loaded only by handlers
/// that actually need its fields (planGuard, settings).
///
/// Scoped: one instance per request.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public const string SchoolIdClaim = "schoolId";
    public const string RoleClaim     = ClaimTypes.Role;
    public const string UserIdClaim   = ClaimTypes.NameIdentifier;

    public Guid? SchoolId { get; }
    public Guid? UserId { get; }
    public string? Role { get; }

    public bool IsSuperAdmin => string.Equals(Role, "superadmin", StringComparison.Ordinal);

    private readonly bool _authenticated;

    /// <summary>
    /// Filter is bypassed ONLY for superadmin.
    ///
    /// An unauthenticated request has no SchoolId, so IsFilterActive stays true
    /// and `e.SchoolId == null` matches NOTHING — anonymous requests see an empty
    /// set by default. That is the safe direction: the three anonymous endpoints
    /// (public share link, Razorpay webhook, login) must each opt out explicitly
    /// via IgnoreQueryFilters() and scope themselves, which greps cleanly.
    ///
    /// The naive `SchoolId.HasValue &amp;&amp; !IsSuperAdmin` version inverts this —
    /// unauthenticated would deactivate the filter and expose every school's
    /// rows to any handler that forgot to authorise. Do not "simplify" it back.
    /// </summary>
    public bool IsFilterActive => !IsSuperAdmin;

    public TenantContext(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;
        _authenticated = true;

        Role = user.FindFirst(RoleClaim)?.Value
               ?? user.FindFirst("role")?.Value;

        if (Guid.TryParse(user.FindFirst(UserIdClaim)?.Value, out var uid))
            UserId = uid;

        var sid = user.FindFirst(SchoolIdClaim)?.Value;
        if (!string.IsNullOrEmpty(sid) && Guid.TryParse(sid, out var schoolId))
            SchoolId = schoolId;
    }

    public Guid RequireSchoolId() =>
        SchoolId ?? throw new Common.AppException(
            "School context required", 403, Common.ErrorCodes.NoSchool);
}

/// <summary>
/// Fixed tenant for background jobs and the seeder, which have no HttpContext.
/// Pass null for SchoolId to run unfiltered (seeder), or a Guid to scope a job.
/// </summary>
public sealed class FixedTenantContext : ITenantContext
{
    public Guid? SchoolId { get; }
    public Guid? UserId => null;
    public string? Role { get; }

    public bool IsSuperAdmin => Role == "superadmin";
    public bool IsFilterActive => SchoolId.HasValue && !IsSuperAdmin;

    public FixedTenantContext(Guid? schoolId, string? role = null)
    {
        SchoolId = schoolId;
        Role = role;
    }

    /// <summary>Unfiltered context — seeder and cross-tenant jobs only.</summary>
    public static FixedTenantContext Unfiltered() => new(null, "superadmin");

    public Guid RequireSchoolId() =>
        SchoolId ?? throw new Common.AppException(
            "School context required", 403, Common.ErrorCodes.NoSchool);
}
