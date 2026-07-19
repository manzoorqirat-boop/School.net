using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features;

/// <summary>
/// Row-level scoping on top of the tenant filter. The global filter guarantees
/// a caller only sees THEIR SCHOOL; this narrows further so a parent sees only
/// their own children and a student only themselves.
///
/// Contract rule (API-CONTRACT §1.1): an out-of-scope LIST is an empty page,
/// NOT a 403 — a parent with no linked children gets {items:[], pagination…}.
/// An out-of-scope single GET, by contrast, IS a 403. Both are honoured here
/// (ApplyStudentScope for lists, CanAccessStudent for single reads).
/// </summary>
public static class RowScope
{
    /// <summary>
    /// Narrows a Student query by role. Admin/teacher/etc. pass through (already
    /// school-scoped); parent → parentOf set; student → self.
    /// Returns an always-empty query for a parent/student with no linkage, so
    /// the caller naturally produces an empty page.
    /// </summary>
    public static IQueryable<Student> ApplyStudentScope(
        IQueryable<Student> q, ITenantContext tenant, IReadOnlyCollection<Guid> parentOf)
    {
        return tenant.Role switch
        {
            "parent" => parentOf.Count == 0
                ? q.Where(_ => false)
                : q.Where(s => parentOf.Contains(s.Id)),

            "student" => tenant.StudentId is { } sid
                ? q.Where(s => s.Id == sid)
                : q.Where(_ => false),

            _ => q,
        };
    }

    /// <summary>
    /// Single-record authorization: true if this caller may see THIS student.
    /// A false result means 403 (the row exists in-tenant but out of the
    /// caller's row-scope) — distinct from 404 (not in tenant at all).
    /// </summary>
    public static bool CanAccessStudent(
        Student s, ITenantContext tenant, IReadOnlyCollection<Guid> parentOf)
    {
        return tenant.Role switch
        {
            "parent" => parentOf.Contains(s.Id),
            "student" => tenant.StudentId == s.Id,
            _ => true,
        };
    }
}
