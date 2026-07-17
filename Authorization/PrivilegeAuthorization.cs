using Microsoft.AspNetCore.Authorization;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Authorization;

/// <summary>Port of `requirePrivilege('x:y')`. Usage: [RequirePrivilege("student:view")]</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePrivilegeAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "priv:";

    public RequirePrivilegeAttribute(string privilege) => Policy = PolicyPrefix + privilege;
}

public sealed class PrivilegeRequirement : IAuthorizationRequirement
{
    public string Privilege { get; }
    public PrivilegeRequirement(string privilege) => Privilege = privilege;
}

/// <summary>
/// Builds a policy on demand for any "priv:*" name, so adding a privilege needs
/// no startup registration — matching the Node app, where requirePrivilege()
/// takes an arbitrary string.
/// </summary>
public sealed class PrivilegePolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PrivilegePolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePrivilegeAttribute.PolicyPrefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var privilege = policyName[RequirePrivilegeAttribute.PolicyPrefix.Length..];

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PrivilegeRequirement(privilege))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>
/// Resolution order, matching `can()` in api.ts and requirePrivilege server-side:
///   1. superadmin  → always allow (short-circuit, no lookup)
///   2. per-school override in role_privileges, if a row exists
///   3. PrivilegeDefaults
///   4. unknown privilege → DENY (fail closed)
/// </summary>
public sealed class PrivilegeHandler : AuthorizationHandler<PrivilegeRequirement>
{
    private readonly ITenantContext _tenant;
    private readonly IPrivilegeResolver _resolver;
    private readonly ILogger<PrivilegeHandler> _log;

    public PrivilegeHandler(
        ITenantContext tenant,
        IPrivilegeResolver resolver,
        ILogger<PrivilegeHandler> log)
    {
        _tenant = tenant;
        _resolver = resolver;
        _log = log;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PrivilegeRequirement req)
    {
        var role = _tenant.Role;
        if (string.IsNullOrEmpty(role)) return;   // unauthenticated → 401 upstream

        // superadmin short-circuits before any DB work — mirrors api.ts can().
        if (_tenant.IsSuperAdmin)
        {
            ctx.Succeed(req);
            return;
        }

        var allowed = await _resolver.RolesForAsync(req.Privilege, _tenant.SchoolId);

        if (allowed is null)
        {
            // Unknown privilege. api.ts logs '[can] unknown privilege' and returns
            // false; we do the same. Fail closed — a typo'd privilege must never
            // become an open endpoint.
            _log.LogWarning("Unknown privilege requested: {Privilege}", req.Privilege);
            return;
        }

        if (allowed.Contains(role, StringComparer.Ordinal))
            ctx.Succeed(req);
    }
}

public interface IPrivilegeResolver
{
    /// <summary>Effective roles for a privilege, or null if the privilege is unknown.</summary>
    Task<IReadOnlyCollection<string>?> RolesForAsync(string privilege, Guid? schoolId);

    /// <summary>Full matrix for GET /api/privileges, incl. isCustomized flags.</summary>
    Task<IReadOnlyDictionary<string, (string[] Roles, bool IsCustomized)>> MatrixAsync(Guid schoolId);
}
