using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;

namespace QMSoft.Api.Authorization;

/// <summary>
/// Resolution: per-school override row if one exists, else PrivilegeDefaults,
/// else null (unknown privilege → PrivilegeHandler fails closed).
///
/// Query notes:
/// - IgnoreQueryFilters + explicit schoolId: this runs INSIDE authorization,
///   before the request is trusted — the resolver must not depend on the very
///   tenant filter whose inputs it is helping validate, and superadmin
///   (schoolId null) short-circuits in the handler before reaching here.
/// - No caching, deliberately. The Node app cached privileges with the same
///   5-minute Redis layer we dropped (README: "start clean, measure") — and a
///   stale privilege grant after an admin revokes it is the same class of hole
///   as the stale auth cache. One indexed PK-ish lookup per authorised request
///   is the price; the unique (school_id, privilege) index makes it cheap.
/// </summary>
public sealed class DbPrivilegeResolver : IPrivilegeResolver
{
    private readonly AppDbContext _db;

    public DbPrivilegeResolver(AppDbContext db) => _db = db;

    public async Task<IReadOnlyCollection<string>?> RolesForAsync(string privilege, Guid? schoolId)
    {
        // Overrides only exist per school; a school-less caller (superadmin is
        // already short-circuited; anonymous never reaches authorization) gets
        // pure defaults.
        if (schoolId is not null)
        {
            var over = await _db.RolePrivileges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.SchoolId == schoolId && r.Privilege == privilege)
                .Select(r => r.Roles)
                .FirstOrDefaultAsync();

            if (over is not null) return over;
        }

        return PrivilegeDefaults.Map.TryGetValue(privilege, out var defaults)
            ? defaults
            : null;                                   // unknown → deny upstream
    }

    /// <summary>
    /// GET /api/privileges response: EVERY default privilege, with the
    /// override applied where a row exists — `isCustomized` marks exactly the
    /// rows that exist in the table. Override rows for privileges that no
    /// longer exist in defaults are ignored, matching the Node controller
    /// (it iterates PRIVILEGES, not the collection).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, (string[] Roles, bool IsCustomized)>>
        MatrixAsync(Guid schoolId)
    {
        var overrides = await _db.RolePrivileges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.SchoolId == schoolId)
            .ToDictionaryAsync(r => r.Privilege, r => r.Roles);

        var matrix = new Dictionary<string, (string[], bool)>(StringComparer.Ordinal);

        foreach (var (priv, defaults) in PrivilegeDefaults.Map)
        {
            matrix[priv] = overrides.TryGetValue(priv, out var roles)
                ? (roles.ToArray(), true)
                : (defaults, false);
        }

        return matrix;
    }
}
