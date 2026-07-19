using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Features.Students;

/// <summary>
/// Port of the students route's parent auto-provisioning helpers. On student
/// create, this either LINKS the new child to an existing parent (matched by
/// phone within the school — sibling linking) or CREATES a parent user with a
/// DOB-derived password shown once to the admin.
///
/// Every failure path returns a result object rather than throwing: parent
/// provisioning must NEVER block student creation (verbatim Node stance).
/// </summary>
public sealed class ParentLinkService
{
    private readonly AppDbContext _db;

    public ParentLinkService(AppDbContext db) => _db = db;

    // "late", "swargiya", Devanagari स्वर्गीय, "(late)", "L." prefix — a
    // deceased father must not become the login parent. Verbatim marker set.
    private static readonly Regex[] DeceasedMarkers =
    {
        new(@"\blate\b", RegexOptions.IgnoreCase),
        new(@"\bswargiy[a]?\b", RegexOptions.IgnoreCase),
        new(@"स्वर्गीय"),
        new(@"\(late\)", RegexOptions.IgnoreCase),
        new(@"\bl\.\s*", RegexOptions.IgnoreCase),
    };

    private static bool IsDeceased(string? name) =>
        string.IsNullOrWhiteSpace(name) || DeceasedMarkers.Any(rx => rx.IsMatch(name));

    public sealed record ParentSource(string Name, string Phone, string SourceKind);

    /// <summary>guardian > father(if alive) > mother. Verbatim precedence.</summary>
    public static ParentSource? PickParentSource(
        string? guardianName, string? guardianPhone,
        string? fatherName, string? fatherPhone,
        string? motherName, string? motherPhone)
    {
        if (!string.IsNullOrWhiteSpace(guardianName))
            return new(guardianName.Trim(), (guardianPhone ?? "").Trim(), "guardian");

        if (!string.IsNullOrWhiteSpace(fatherName) && !IsDeceased(fatherName))
            return new(fatherName.Trim(), (fatherPhone ?? "").Trim(), "father");

        if (!string.IsNullOrWhiteSpace(motherName))
            return new(motherName.Trim(), (motherPhone ?? "").Trim(), "mother");

        return null;
    }

    private static readonly Regex Honorifics =
        new(@"\b(mr|mrs|ms|dr|shri|smt|श्री|श्रीमती)\b\.?", RegexOptions.IgnoreCase);

    internal static string SlugifyName(string? name)
    {
        var s = (name ?? "").ToLowerInvariant();
        s = Regex.Replace(s, @"\([^)]*\)", "");     // strip "(late)" etc.
        s = Honorifics.Replace(s, "");
        s = Regex.Replace(s, @"\s+", "");
        s = Regex.Replace(s, @"[^a-z0-9]", "");
        return s.Length > 40 ? s[..40] : s;
    }

    /// <summary>Password@DDMMYYYY from DOB, or null if no valid DOB.</summary>
    internal static string? DobPassword(DateOnly? dob) =>
        dob is { } d ? $"Password@{d.Day:D2}{d.Month:D2}{d.Year}" : null;

    private async Task<string> NextUniqueUsernameAsync(string? baseName, Guid schoolId, CancellationToken ct)
    {
        var b = string.IsNullOrEmpty(baseName) ? "parent" : baseName;
        var candidate = b;
        var n = 2;
        while (await _db.Users.IgnoreQueryFilters()
                   .AnyAsync(u => u.Username == candidate && u.SchoolId == schoolId, ct))
        {
            candidate = $"{b}{n}";
            if (++n > 100) { candidate = $"{b}{DateTime.UtcNow.Ticks.ToString()[^5..]}"; break; }
        }
        return candidate;
    }

    public sealed record LinkResult(
        bool IsNew = false, bool Linked = false, bool Skipped = false,
        string? Username = null, string? PlaintextPassword = null,
        string? Source = null, string? Reason = null, string? Error = null,
        Guid? UserId = null, string? ParentName = null);

    public async Task<LinkResult> CreateOrLinkAsync(
        Student student, Guid schoolId, string? schoolSlug,
        ParentSource? src, CancellationToken ct)
    {
        try
        {
            if (src is null || string.IsNullOrWhiteSpace(src.Name))
                return new(Skipped: true, Reason: "no_parent_info");

            // Sibling linking by phone.
            if (!string.IsNullOrEmpty(src.Phone))
            {
                var existing = await _db.Users.IgnoreQueryFilters()
                    .Include(u => u.ParentOf)
                    .FirstOrDefaultAsync(u =>
                        u.SchoolId == schoolId && u.Role == UserRole.Parent && u.Phone == src.Phone, ct);

                if (existing is not null)
                {
                    if (existing.ParentOf.All(s => s.Id != student.Id))
                    {
                        existing.ParentOf.Add(student);
                        await _db.SaveChangesAsync(ct);
                    }
                    return new(IsNew: false, Linked: true, UserId: existing.Id, ParentName: existing.Name);
                }
            }

            var username = await NextUniqueUsernameAsync(SlugifyName(src.Name), schoolId, ct);
            var password = DobPassword(student.Dob);
            if (password is null)
                return new(Skipped: true, Reason: "no_dob");

            var user = new User
            {
                SchoolId = schoolId,
                SchoolSlug = schoolSlug,
                Username = username,
                Password = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10),
                Role = UserRole.Parent,
                Name = src.Name,
                Phone = string.IsNullOrEmpty(src.Phone) ? null : src.Phone,
                IsActive = true,
            };
            user.ParentOf.Add(student);

            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            return new(IsNew: true, Username: username, PlaintextPassword: password,
                       Source: src.SourceKind, UserId: user.Id);
        }
        catch (Exception ex)
        {
            return new(Error: ex.Message);   // never blocks student creation
        }
    }
}
