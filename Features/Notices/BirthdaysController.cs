using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Notices;

/// <summary>
/// Today's and upcoming birthdays for the dashboard widget.
///
/// Deliberately returns DAY AND MONTH ONLY, never the year. The widget needs
/// "whose birthday is it", not "how old are they" — and staff dates of birth
/// sitting in a payroll table are not something to hand to every teacher who
/// loads a dashboard. The year never leaves the server.
/// </summary>
[ApiController]
[Route("api/birthdays")]
[Authorize]
public sealed class BirthdaysController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public BirthdaysController(AppDbContext db, ITenantContext tenant)
    { _db = db; _tenant = tenant; }

    /// <summary>
    /// Roles whose birthdays appear in the staff list. Parents are excluded —
    /// they are users too, and a parent's birthday on the staff-room dashboard
    /// is not what anyone means by "staff birthdays".
    /// </summary>
    private static readonly UserRole[] StaffRoles =
        [UserRole.Teacher, UserRole.Principal, UserRole.SchoolAdmin, UserRole.Accountant];

    [HttpGet]
    [RequirePrivilege("birthday:view")]
    public async Task<IActionResult> Get(
        [FromQuery] int days = 7,
        [FromQuery] int tzOffsetMinutes = 330,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 31);

        // There is no per-school timezone column anywhere in this schema, so
        // "today" is resolved from an offset the client supplies
        // (-new Date().getTimezoneOffset()). Default 330 = IST: with the server
        // on UTC, a straight UtcNow.Date would roll the widget over to tomorrow
        // at 18:30 local and show the wrong people for five and a half hours
        // every single day.
        tzOffsetMinutes = Math.Clamp(tzOffsetMinutes, -840, 840);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(tzOffsetMinutes));

        // Month/day keys for the window, as MMDD ints. Comparing on a composite
        // int sidesteps the year entirely — which is the whole point — and
        // wraps across December without special-casing.
        var window = new List<(DateOnly Date, int Key)>();
        var keys = new List<int>();
        for (var i = 0; i < days; i++)
        {
            var d = today.AddDays(i);
            var key = d.Month * 100 + d.Day;
            window.Add((d, key));
            keys.Add(key);

            // 29 Feb exists in the data but not in most years. Fold it onto
            // 1 March so those birthdays are not silently skipped three years
            // out of four.
            if (d.Month == 3 && d.Day == 1 && !DateTime.IsLeapYear(d.Year))
            {
                window.Add((d, 229));
                keys.Add(229);
            }
        }

        var students = await _db.Students.AsNoTracking()
            .Where(s => !s.IsDeleted
                     && s.Status == StudentStatus.Active
                     && s.Dob != null
                     && keys.Contains(s.Dob!.Value.Month * 100 + s.Dob!.Value.Day))
            .Select(s => new
            {
                s.Id,
                // FirstName/LastName, not DisplayName: that is a computed C#
                // property with no column behind it, so EF cannot translate it.
                // Joined in memory below.
                s.FirstName,
                s.LastName,
                s.Class,
                s.Section,
                Month = s.Dob!.Value.Month,
                Day   = s.Dob!.Value.Day,
            })
            .ToListAsync(ct);

        var staff = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive
                     && u.Dob != null
                     && StaffRoles.Contains(u.Role)
                     && keys.Contains(u.Dob!.Value.Month * 100 + u.Dob!.Value.Day))
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Role,
                Month = u.Dob!.Value.Month,
                Day   = u.Dob!.Value.Day,
            })
            .ToListAsync(ct);

        var people = students
            .Select(s => new
            {
                _id      = s.Id,
                Name     = string.Join(' ', new[] { s.FirstName, s.LastName }
                                              .Where(x => !string.IsNullOrWhiteSpace(x))),
                type     = "student",
                @class   = (string?)s.Class,
                Section  = (string?)s.Section,
                role     = (string?)null,
                s.Month,
                s.Day,
            })
            .Concat(staff.Select(u => new
            {
                _id      = u.Id,
                u.Name,
                type     = "staff",
                @class   = (string?)null,
                Section  = (string?)null,
                role     = (string?)EnumWireParse.ToWire(u.Role),
                u.Month,
                u.Day,
            }))
            .ToList();

        // Group back onto the window so the client gets calendar order rather
        // than having to re-derive "is this today" from a bare month/day.
        var byDate = window
            .GroupBy(w => w.Date)
            .Select(g => new
            {
                date    = g.Key.ToString("yyyy-MM-dd"),
                isToday = g.Key == today,
                people  = people
                    .Where(p => g.Any(w => w.Key == p.Month * 100 + p.Day))
                    .OrderBy(p => p.type)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .Where(x => x.people.Count > 0)
            .OrderBy(x => x.date)
            .ToList();

        // Built by SelectMany rather than FirstOrDefault(...)?.people ?? [] —
        // the empty collection expression has no nameable anonymous element
        // type to target.
        var todayPeople = byDate.Where(x => x.isToday)
                                .SelectMany(x => x.people)
                                .ToList();

        return Ok(new
        {
            today = todayPeople,
            upcoming = byDate.Where(x => !x.isToday).ToList(),
            totalCount = people.Count,
        });
    }
}
