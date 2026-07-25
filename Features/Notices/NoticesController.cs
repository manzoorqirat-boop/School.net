using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Notices;

[ApiController]
[Route("api/notices")]
[Authorize]
public sealed class NoticesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public NoticesController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    /// <summary>Admin roles see everything: drafts, expired, soft-deleted excepted.</summary>
    private static bool IsNoticeAdmin(string? role) =>
        role is "school_admin" or "principal" or "superadmin";

    /// <summary>
    /// Upper bound on rows pulled before the class filter runs in memory.
    ///
    /// The role and time-window predicates translate to SQL cleanly. The class
    /// predicate does not: it is an array-overlap between a column and a
    /// runtime-built list, which EF cannot parameterise into `&&` reliably. A
    /// notice board is tens of live rows per school, not thousands, so filtering
    /// the tail in memory is cheaper than the alternatives and cannot go
    /// quadratic. If a school ever exceeds this, the cap truncates the OLDEST
    /// notices, never the pinned or recent ones — the ordering runs first.
    /// </summary>
    private const int ScanCap = 500;

    // ── Read ──────────────────────────────────────────────────────────────

    [HttpGet]
    [RequirePrivilege("notice:view")]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 50,
        [FromQuery] bool includeExpired = false,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var role = _tenant.Role ?? "";
        var isAdmin = IsNoticeAdmin(role);
        var now = DateTime.UtcNow;

        var q = _db.Notices.AsNoTracking().Where(n => !n.IsDeleted);

        if (!isAdmin || !includeExpired)
        {
            // Mirror of Notice.IsLive, expressed so it translates to SQL.
            q = q.Where(n =>
                (n.PublishAt == null || n.PublishAt <= now) &&
                (n.ExpiresAt == null || n.ExpiresAt > now));
        }

        if (!isAdmin)
        {
            // Empty target_roles = everyone. cardinality(...) = 0 in SQL.
            q = q.Where(n => n.TargetRoles.Count == 0 || n.TargetRoles.Contains(role));
        }

        var rows = await q
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .Take(ScanCap)
            .ToListAsync(ct);

        if (!isAdmin)
        {
            var classes = await CallerClassesAsync(role, ct);
            rows = rows.Where(n => n.TargetClasses.Count == 0
                                || n.TargetClasses.Any(classes.Contains)).ToList();
        }

        var page = rows.Take(limit).ToList();
        var authors = await AuthorNamesAsync(page, ct);

        // Bare array — matches the shape every other list page consumes.
        return Ok(page.Select(n => Wire(n, authors)));
    }

    [HttpGet("{id:guid}")]
    [RequirePrivilege("notice:view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var n = await _db.Notices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (n is null) return NotFound(new { error = "Not found" });

        // A direct link must not leak what the list would have hidden.
        if (!IsNoticeAdmin(_tenant.Role) && !await IsVisibleToCallerAsync(n, ct))
            return NotFound(new { error = "Not found" });

        return Ok(Wire(n, await AuthorNamesAsync([n], ct)));
    }

    // ── Write ─────────────────────────────────────────────────────────────

    public sealed class NoticeInput
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Priority { get; set; }
        public List<string>? TargetRoles { get; set; }
        public List<string>? TargetClasses { get; set; }
        public DateTime? PublishAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool? IsPinned { get; set; }
    }

    [HttpPost]
    [RequirePrivilege("notice:manage")]
    public async Task<IActionResult> Create([FromBody] NoticeInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new AppException("Title is required", 400, ErrorCodes.ValidationError);
        if (string.IsNullOrWhiteSpace(input.Body))
            throw new AppException("Body is required", 400, ErrorCodes.ValidationError);

        var n = new Notice
        {
            SchoolId        = _tenant.SchoolId ?? Guid.Empty,
            Title           = input.Title.Trim(),
            Body            = input.Body.Trim(),
            Priority        = ParsePriority(input.Priority),
            TargetRoles     = Clean(input.TargetRoles),
            TargetClasses   = Clean(input.TargetClasses),
            PublishAt       = Utc(input.PublishAt),
            ExpiresAt       = Utc(input.ExpiresAt),
            IsPinned        = input.IsPinned ?? false,
            CreatedByUserId = _tenant.UserId,
        };

        ValidateWindow(n);

        _db.Notices.Add(n);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("notice.create", "Notice", n.Id.ToString(), ct: ct);

        return Ok(Wire(n, await AuthorNamesAsync([n], ct)));
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("notice:manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] NoticeInput input, CancellationToken ct)
    {
        var n = await _db.Notices.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (n is null) return NotFound(new { error = "Not found" });

        if (input.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(input.Title))
                throw new AppException("Title cannot be empty", 400, ErrorCodes.ValidationError);
            n.Title = input.Title.Trim();
        }
        if (input.Body is not null)
        {
            if (string.IsNullOrWhiteSpace(input.Body))
                throw new AppException("Body cannot be empty", 400, ErrorCodes.ValidationError);
            n.Body = input.Body.Trim();
        }

        if (input.Priority      is not null) n.Priority      = ParsePriority(input.Priority);
        if (input.TargetRoles   is not null) n.TargetRoles   = Clean(input.TargetRoles);
        if (input.TargetClasses is not null) n.TargetClasses = Clean(input.TargetClasses);
        if (input.IsPinned      is not null) n.IsPinned      = input.IsPinned.Value;

        // Explicit nulls are meaningful here ("clear the expiry"), but so is
        // omission. The distinction is lost on a plain DTO, so both dates are
        // taken as-sent whenever the caller supplied EITHER — the clients
        // always round-trip the full object on edit.
        if (input.PublishAt is not null || input.ExpiresAt is not null)
        {
            n.PublishAt = Utc(input.PublishAt);
            n.ExpiresAt = Utc(input.ExpiresAt);
        }

        ValidateWindow(n);

        n.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("notice.update", "Notice", n.Id.ToString(), ct: ct);

        return Ok(Wire(n, await AuthorNamesAsync([n], ct)));
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("notice:manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var n = await _db.Notices.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (n is null) return NotFound(new { error = "Not found" });

        // Soft delete — hard deletes are never performed (ISoftDeletable).
        n.IsDeleted = true;
        n.DeletedAt = DateTime.UtcNow;
        n.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("notice.delete", "Notice", n.Id.ToString(), ct: ct);

        return Ok(new { success = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Coerces an inbound date to UTC before it reaches Npgsql.
    ///
    /// Both clients send dates as bare "YYYY-MM-DD" (DateField's output).
    /// System.Text.Json deserialises that to a DateTime with
    /// Kind=Unspecified, and EF maps DateTime? to `timestamp with time zone`.
    /// Npgsql REFUSES that combination and throws:
    ///
    ///   ArgumentException: Cannot write DateTime with Kind=Unspecified to
    ///   PostgreSQL type 'timestamp with time zone'
    ///
    /// That is not a PostgresException, so ExceptionHandlingMiddleware cannot
    /// unwrap it into anything useful — the client just gets a bare
    /// "Internal server error" while every read path keeps working.
    ///
    /// A date with no time is treated as midnight UTC rather than shifted by
    /// the server's offset: publish/expiry are day-granular decisions, and
    /// silently moving "expires on the 30th" by 5½ hours is worse than the
    /// half-day of imprecision.
    ///
    /// NOTE: the same hazard exists on Poll.StartDate/EndDate, which pass
    /// straight through unconverted. It has not fired only because nothing
    /// currently posts a date to that endpoint.
    /// </summary>
    private static DateTime? Utc(DateTime? d) => d is null ? null : d.Value.Kind switch
    {
        DateTimeKind.Utc   => d,
        DateTimeKind.Local => d.Value.ToUniversalTime(),
        _                  => DateTime.SpecifyKind(d.Value, DateTimeKind.Utc),
    };

    private static NoticePriority ParsePriority(string? wire) => wire switch
    {
        null or "" or "normal" => NoticePriority.Normal,
        "important"            => NoticePriority.Important,
        "urgent"               => NoticePriority.Urgent,
        _ => throw new AppException($"Unknown priority '{wire}'", 400, ErrorCodes.ValidationError),
    };

    private static List<string> Clean(List<string>? xs) =>
        xs is null ? []
        : xs.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static void ValidateWindow(Notice n)
    {
        // Mirrors ck_notices_window so the caller gets a 400 with a usable
        // message instead of a 23514 surfaced as a generic constraint error.
        if (n.ExpiresAt is not null && n.PublishAt is not null && n.ExpiresAt <= n.PublishAt)
            throw new AppException("Expiry must be after the publish date",
                400, ErrorCodes.ValidationError);
    }

    /// <summary>
    /// Classes the caller belongs to, for the class-targeting axis.
    ///
    /// Only student and parent are class-bound. Every other role returns empty,
    /// which the callers read as "not class-constrained" — see the Notice
    /// entity comment for why staff are exempt rather than filtered.
    /// </summary>
    private async Task<HashSet<string>> CallerClassesAsync(string role, CancellationToken ct)
    {
        if (role == "student")
        {
            var sid = _tenant.StudentId;
            if (sid is null) return [];
            var cls = await _db.Students.AsNoTracking()
                .Where(s => s.Id == sid)
                .Select(s => s.Class)
                .FirstOrDefaultAsync(ct);
            return string.IsNullOrWhiteSpace(cls) ? [] : [cls];
        }

        if (role == "parent")
        {
            var uid = _tenant.UserId;
            if (uid is null) return [];
            var classes = await _db.Users.AsNoTracking()
                .Where(u => u.Id == uid)
                .SelectMany(u => u.ParentOf)
                .Select(s => s.Class)
                .ToListAsync(ct);
            return classes.Where(c => !string.IsNullOrWhiteSpace(c))
                          .ToHashSet(StringComparer.Ordinal);
        }

        return [];
    }

    private async Task<bool> IsVisibleToCallerAsync(Notice n, CancellationToken ct)
    {
        var role = _tenant.Role ?? "";
        if (!n.IsLive(DateTime.UtcNow)) return false;
        if (n.TargetRoles.Count > 0 && !n.TargetRoles.Contains(role)) return false;
        if (n.TargetClasses.Count == 0) return true;

        var classes = await CallerClassesAsync(role, ct);
        return n.TargetClasses.Any(classes.Contains);
    }

    /// <summary>Author names in one grouped query — the list would otherwise be
    /// one lookup per row.</summary>
    private async Task<Dictionary<Guid, string>> AuthorNamesAsync(
        IReadOnlyCollection<Notice> notices, CancellationToken ct)
    {
        var ids = notices.Where(n => n.CreatedByUserId is not null)
                         .Select(n => n.CreatedByUserId!.Value)
                         .Distinct().ToList();
        if (ids.Count == 0) return [];

        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }

    private static object Wire(Notice n, IReadOnlyDictionary<Guid, string> authors) => new
    {
        _id = n.Id,
        n.Title,
        n.Body,
        priority = EnumWireParse.ToWire(n.Priority),
        n.TargetRoles,
        n.TargetClasses,
        n.PublishAt,
        n.ExpiresAt,
        n.IsPinned,
        createdBy = n.CreatedByUserId,
        createdByName = n.CreatedByUserId is Guid g && authors.TryGetValue(g, out var nm) ? nm : null,
        n.CreatedAt,
        n.UpdatedAt,
    };
}
