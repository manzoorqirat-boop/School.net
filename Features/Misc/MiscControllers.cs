using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Misc;

// ── Polls ─────────────────────────────────────────────────────────────────
[ApiController]
[Route("api/polls")]
[Authorize]
public sealed class PollsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public PollsController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    private static bool IsPollAdmin(string? role) =>
        role is "school_admin" or "principal" or "superadmin";

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        // Targeted to the caller's role, or created by admin roles (who see all).
        var role = _tenant.Role ?? "";
        var q = _db.Polls.AsNoTracking().Include(p => p.Questions).ThenInclude(x => x.Options).AsQueryable();
        if (!IsPollAdmin(role))
            q = q.Where(p => p.TargetRoles.Contains(role) && p.Status == PollStatus.Active);

        var polls = await q.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var ids = polls.Select(p => p.Id).ToList();

        // hasVoted + totalVotes per poll. The Node original returned both and
        // the port dropped them, so nothing could tell an answered poll from an
        // unanswered one — a parent saw no "Voted" badge, and the dashboard had
        // no way to surface polls still waiting on them.
        //
        // Two grouped queries rather than one per poll: a class with 40 parents
        // and 10 polls would otherwise be 20 round trips.
        var uid = _tenant.UserId;
        var votedIds = uid is null
            ? new HashSet<Guid>()
            : (await _db.PollVotes.AsNoTracking()
                .Where(v => ids.Contains(v.PollId) && v.UserId == uid)
                .Select(v => v.PollId).ToListAsync(ct)).ToHashSet();

        var counts = await _db.PollVotes.AsNoTracking()
            .Where(v => ids.Contains(v.PollId))
            .GroupBy(v => v.PollId)
            .Select(g => new { PollId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(x => x.PollId, x => x.Count);

        // Bare array — the page does (data || []).map(...).
        return Ok(polls.Select(p => PollWithVoteState(p, votedIds.Contains(p.Id),
            countMap.GetValueOrDefault(p.Id))));
    }

    /// <summary>
    /// Serialises a poll with the caller-specific vote state attached. Built by
    /// hand rather than with an anonymous spread because C# has no object
    /// spread — every field the client reads has to be listed.
    /// </summary>
    private static object PollWithVoteState(Poll p, bool hasVoted, int totalVotes) => new
    {
        _id = p.Id,
        p.Title,
        p.Description,
        category = EnumWireParse.ToWire(p.Category),
        status = EnumWireParse.ToWire(p.Status),
        p.TargetRoles,
        p.StartDate,
        p.EndDate,
        p.ShowResultsBeforeClose,
        p.AllowAnonymous,
        p.CreatedAt,
        questions = p.Questions.Select(q => new
        {
            _id = q.Id,
            q.Text,
            options = q.Options.Select(o => new { _id = o.Id, o.Text }),
        }),
        hasVoted,
        totalVotes,
    };

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await _db.Polls.AsNoTracking().Include(x => x.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });

        var uid = _tenant.UserId;
        var myVote = uid is null ? null : await _db.PollVotes.AsNoTracking()
            .Include(v => v.Answers)
            .FirstOrDefaultAsync(v => v.PollId == id && v.UserId == uid, ct);

        // Results visibility, ported from the Node original: admins always,
        // everyone else only when the poll says so or after it closes. Without
        // this an ordinary voter could read live tallies on a poll configured
        // to hide them until close.
        var canSeeResults = IsPollAdmin(_tenant.Role)
            || p.ShowResultsBeforeClose
            || p.Status == PollStatus.Closed;

        object? results = null;
        var totalVotes = await _db.PollVotes.AsNoTracking().CountAsync(v => v.PollId == id, ct);

        if (canSeeResults)
        {
            var tallies = await _db.PollVoteAnswers.AsNoTracking()
                .Where(a => a.Vote.PollId == id)
                .GroupBy(a => new { a.QuestionId, a.OptionId })
                .Select(g => new { g.Key.QuestionId, g.Key.OptionId, Count = g.Count() })
                .ToListAsync(ct);

            // Zero-filled nested map: results[questionId][optionId] = count.
            // Filling every option matters — an option nobody picked must read
            // 0, not be absent, or the client's percentage maths divides wrong.
            var map = new Dictionary<string, Dictionary<string, int>>();
            foreach (var q in p.Questions)
            {
                var inner = new Dictionary<string, int>();
                foreach (var o in q.Options) inner[o.Id.ToString()] = 0;
                map[q.Id.ToString()] = inner;
            }
            foreach (var t in tallies)
            {
                var qk = t.QuestionId.ToString();
                var ok = t.OptionId.ToString();
                if (map.TryGetValue(qk, out var inner) && inner.ContainsKey(ok)) inner[ok] = t.Count;
            }
            results = map;
        }

        var basePoll = PollWithVoteState(p, myVote is not null, totalVotes);

        return Ok(new
        {
            poll = basePoll,
            hasVoted = myVote is not null,
            myVote = myVote is null ? null : new
            {
                _id = myVote.Id,
                answers = myVote.Answers.Select(a => new { questionId = a.QuestionId, optionId = a.OptionId }),
                votedAt = myVote.SubmittedAt,   // PollVote has SubmittedAt, not CreatedAt
            },
            results,
            totalVotes,
            canSeeResults,
        });
    }

    [HttpPost]
    // Node gated poll writes on authenticate only (no privilege) — match that.
    public async Task<IActionResult> Create([FromBody] PollWriteRequest req, CancellationToken ct)
    {
        var missing = new List<object>();
        if (string.IsNullOrWhiteSpace(req.Title))
            missing.Add(new { field = "title", message = "Title is required." });
        if (!EnumWireParse.TryOptional<PollCategory>(req.Category, out var cat))
            missing.Add(new { field = "category", message = $"Must be one of: {EnumWireParse.Allowed<PollCategory>()}." });
        if (!EnumWireParse.TryOptional<PollStatus>(req.Status, out var pollStatus))
            missing.Add(new { field = "status", message = $"Must be one of: {EnumWireParse.Allowed<PollStatus>()}." });
        if (missing.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = missing });

        var body = new Poll
        {
            Title = req.Title!.Trim(),
            Description = req.Description,
            Category = cat ?? PollCategory.General,
            Status = pollStatus ?? PollStatus.Draft,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            ShowResultsBeforeClose = req.ShowResultsBeforeClose ?? true,
            AllowAnonymous = req.AllowAnonymous ?? false,
        };
        if (req.TargetRoles is { Count: > 0 }) body.TargetRoles = req.TargetRoles;

        foreach (var q in req.Questions ?? [])
        {
            var pq = new PollQuestion { Text = q.Text ?? "" };
            foreach (var o in q.Options ?? [])
                pq.Options.Add(new PollOption { Text = o.Text ?? "" });
            body.Questions.Add(pq);
        }

        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        body.CreatedByUserId = _tenant.UserId;
        body.Validate();                                  // ≥1 question, ≥2 options each
        _db.Polls.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("poll.create", "poll", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    // Node gated poll writes on authenticate only (no privilege) — match that.
    public async Task<IActionResult> Update(Guid id, [FromBody] PollWriteRequest req, CancellationToken ct)
    {
        var p = await _db.Polls.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });

        // Meta only — questions/options are not edited through this route
        // (votes reference option ids; rewriting them would orphan ballots).
        var bad = new List<object>();
        if (!EnumWireParse.TryOptional<PollCategory>(req.Category, out var cat))
            bad.Add(new { field = "category", message = $"Must be one of: {EnumWireParse.Allowed<PollCategory>()}." });
        if (!EnumWireParse.TryOptional<PollStatus>(req.Status, out var newStatus))
            bad.Add(new { field = "status", message = $"Must be one of: {EnumWireParse.Allowed<PollStatus>()}." });
        if (bad.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = bad });

        if (!string.IsNullOrWhiteSpace(req.Title)) p.Title = req.Title.Trim();
        if (cat is { } c) p.Category = c;
        if (newStatus is { } st) p.Status = st;
        if (req.TargetRoles is { Count: > 0 }) p.TargetRoles = req.TargetRoles;
        if (req.ShowResultsBeforeClose is { } sr) p.ShowResultsBeforeClose = sr;
        if (req.AllowAnonymous is { } aa) p.AllowAnonymous = aa;
        p.Description = req.Description;
        p.StartDate = req.StartDate; p.EndDate = req.EndDate;
        await _db.SaveChangesAsync(ct);
        return Ok(p);
    }

    [HttpDelete("{id:guid}")]
    // Node gated poll writes on authenticate only (no privilege) — match that.
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await _db.Polls.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });
        _db.Polls.Remove(p);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    public sealed record VoteAnswer(Guid QuestionId, Guid OptionId);
    public sealed record VoteRequest(List<VoteAnswer>? Answers);

    [HttpPost("{id:guid}/vote")]
    public async Task<IActionResult> Vote(Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
        if (_tenant.UserId is not { } uid) return Unauthorized(new { error = "Not authenticated" });
        if (req.Answers is null || req.Answers.Count == 0) return BadRequest(new { error = "answers required" });

        var vote = new PollVote
        {
            SchoolId = _tenant.SchoolId ?? Guid.Empty, PollId = id, UserId = uid,
            UserName = User.Identity?.Name, UserRole = _tenant.Role,
            Answers = req.Answers.Select(a => new PollVoteAnswer { QuestionId = a.QuestionId, OptionId = a.OptionId }).ToList(),
        };
        _db.PollVotes.Add(vote);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)   // uq_poll_votes_one_per_user → 23505
        { return Conflict(new { error = "You have already voted in this poll", code = ErrorCodes.DuplicateError }); }

        await _audit.WriteAsync("poll.vote", "poll", id.ToString(), ct: ct);
        return StatusCode(201, new { ok = true });
    }

    [HttpGet("{id:guid}/results")]
    public async Task<IActionResult> Results(Guid id, CancellationToken ct)
    {
        var tallies = await _db.PollVoteAnswers.AsNoTracking()
            .Where(a => a.Vote.PollId == id)
            .GroupBy(a => new { a.QuestionId, a.OptionId })
            .Select(g => new { g.Key.QuestionId, g.Key.OptionId, count = g.Count() })
            .ToListAsync(ct);
        var totalVotes = await _db.PollVotes.AsNoTracking().CountAsync(v => v.PollId == id, ct);
        return Ok(new { pollId = id, totalVotes, tallies });
    }
}

// ── Privileges matrix ───────────────────────────────────────────────────────
[ApiController]
[Route("api/privileges")]
public sealed class PrivilegesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPrivilegeResolver _resolver;
    private readonly IAuditWriter _audit;
    public PrivilegesController(AppDbContext db, ITenantContext tenant, IPrivilegeResolver resolver, IAuditWriter audit)
    { _db = db; _tenant = tenant; _resolver = resolver; _audit = audit; }

    [HttpGet]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> GetMatrix(CancellationToken ct)
    {
        var roles = new[] { "school_admin","principal","accountant","teacher","parent","student" };  // superadmin excluded

        if (_tenant.SchoolId is not { } sid)
            return Ok(new { matrix = new Dictionary<string, object>(), availableRoles = roles, privileges = PrivilegeDefaults.Map.Keys });

        var m = await _resolver.MatrixAsync(sid);
        var matrix = m.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                roles = kv.Value.Roles,
                isCustomized = kv.Value.IsCustomized,
                @default = PrivilegeDefaults.Map.GetValueOrDefault(kv.Key) ?? Array.Empty<string>(),
            });
        return Ok(new { matrix, availableRoles = roles, privileges = PrivilegeDefaults.Map.Keys });
    }

    public sealed record UpdatePrivilegeRequest(List<string> Roles);
    [HttpPut("{privilege}")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> Update(string privilege, [FromBody] UpdatePrivilegeRequest req, CancellationToken ct)
    {
        var sid = _tenant.SchoolId ?? Guid.Empty;
        var row = await _db.RolePrivileges.FirstOrDefaultAsync(r => r.SchoolId == sid && r.Privilege == privilege, ct);
        if (row is null)
        {
            row = new RolePrivilege { SchoolId = sid, Privilege = privilege, Roles = req.Roles, UpdatedByUserId = _tenant.UserId };
            _db.RolePrivileges.Add(row);
        }
        else { row.Roles = req.Roles; row.UpdatedByUserId = _tenant.UserId; row.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("privilege.update", "role_privilege", privilege, ct: ct);
        return Ok(new { privilege, roles = req.Roles, isCustomized = true });
    }

    [HttpPost("{privilege}/reset")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> Reset(string privilege, CancellationToken ct)
    {
        var sid = _tenant.SchoolId ?? Guid.Empty;
        var row = await _db.RolePrivileges.FirstOrDefaultAsync(r => r.SchoolId == sid && r.Privilege == privilege, ct);
        if (row is not null) { _db.RolePrivileges.Remove(row); await _db.SaveChangesAsync(ct); }
        var defaults = PrivilegeDefaults.Map.GetValueOrDefault(privilege) ?? Array.Empty<string>();
        return Ok(new { privilege, roles = defaults, isCustomized = false });
    }

    [HttpPost("role/{role}/reset")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> ResetRole(string role, CancellationToken ct)
    {
        var valid = new[] { "superadmin","school_admin","principal","accountant","teacher","parent","student" };
        if (!valid.Contains(role)) return BadRequest(new { error = $"Invalid role: {role}" });

        var sid = _tenant.SchoolId ?? Guid.Empty;
        // Remove this role from every customized override so all privileges fall
        // back to defaults for that role. Count privileges touched.
        var overrides = await _db.RolePrivileges.Where(r => r.SchoolId == sid).ToListAsync(ct);
        int touched = 0;
        foreach (var o in overrides)
        {
            var defaults = PrivilegeDefaults.Map.GetValueOrDefault(o.Privilege) ?? Array.Empty<string>();
            var wantsRole = defaults.Contains(role);
            var hasRole = o.Roles.Contains(role);
            if (wantsRole && !hasRole) { o.Roles = o.Roles.Append(role).ToList(); touched++; }
            else if (!wantsRole && hasRole) { o.Roles = o.Roles.Where(x => x != role).ToList(); touched++; }
        }
        if (touched > 0) await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("privilege.reset_role", "role_privilege", role, ct: ct);
        return Ok(new { privilegesTouched = touched });
    }

    [HttpPost("reset-all")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> ResetAll(CancellationToken ct)
    {
        var sid = _tenant.SchoolId ?? Guid.Empty;
        var rows = await _db.RolePrivileges.Where(r => r.SchoolId == sid).ToListAsync(ct);
        _db.RolePrivileges.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("privilege.reset_all", "role_privilege", ct: ct);
        return Ok(new { ok = true, removed = rows.Count });
    }
}

// ── Audit logs ──────────────────────────────────────────────────────────────
[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditLogsController(AppDbContext db) => _db = db;

    [HttpGet]
    [RequirePrivilege("audit:view")]
    public async Task<IActionResult> List([FromQuery] string? action, [FromQuery] string? entity,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? limit, [FromQuery] int? skip, CancellationToken ct)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(entity)) q = q.Where(a => a.Entity == entity);
        if (from is { } f) q = q.Where(a => a.CreatedAt >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(a => a.CreatedAt <= t.ToDateTime(TimeOnly.MaxValue));

        var l = Math.Clamp(limit ?? 100, 1, 500);
        var sk = Math.Max(0, skip ?? 0);
        var total = await q.CountAsync(ct);
        // { logs, total, limit, skip } — verbatim Node shape (page reads r.logs/r.total).
        var logs = await q.OrderByDescending(a => a.CreatedAt).Skip(sk).Take(l).ToListAsync(ct);
        return Ok(new { logs, total, limit = l, skip = sk });
    }

    [HttpGet("actions")]
    [RequirePrivilege("audit:view")]
    public async Task<IActionResult> Actions(CancellationToken ct) =>
        // Bare string[] — page types it as string[].
        Ok(await _db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct));

    [HttpGet("stats")]
    [RequirePrivilege("audit:view")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var total = await _db.AuditLogs.AsNoTracking().CountAsync(ct);
        var byAction = await _db.AuditLogs.AsNoTracking()
            .GroupBy(a => a.Action)
            .Select(g => new { _id = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).Take(20).ToListAsync(ct);
        var byUser = await _db.AuditLogs.AsNoTracking()
            .GroupBy(a => a.Username)
            .Select(g => new { _id = new { username = g.Key }, count = g.Count() })
            .OrderByDescending(x => x.count).Take(20).ToListAsync(ct);
        return Ok(new { total, byAction, byUser });
    }
}

// ── Class teachers ────────────────────────────────────────────────────────
[ApiController]
[Route("api/class-teachers")]
public sealed class ClassTeachersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    public ClassTeachersController(AppDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

    [HttpGet]
    [RequirePrivilege("teacher:view")]
    public async Task<IActionResult> List([FromQuery] string? academicYear, CancellationToken ct)
    {
        var q = _db.ClassTeachers.AsNoTracking().Where(c => c.IsActive);
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(c => c.AcademicYear == academicYear);
        return Ok(new { items = await q.ToListAsync(ct) });
    }

    [HttpGet("my-classes")]
    [Authorize]
    public async Task<IActionResult> MyClasses(CancellationToken ct)
    {
        if (_tenant.UserId is not { } uid) return Ok(new { items = Array.Empty<object>() });
        var items = await _db.ClassTeachers.AsNoTracking()
            .Where(c => c.TeacherUserId == uid && c.IsActive).ToListAsync(ct);
        return Ok(new { items });
    }

    [HttpPost]
    [RequirePrivilege("teacher:manage")]
    public async Task<IActionResult> Create([FromBody] ClassTeacherWriteRequest req, CancellationToken ct)
    {
        var missing = new List<object>();
        if (req.TeacherUserId is null)             missing.Add(new { field = "teacherUserId", message = "Teacher is required." });
        if (string.IsNullOrWhiteSpace(req.Class))  missing.Add(new { field = "class",         message = "Class is required." });
        if (string.IsNullOrWhiteSpace(req.Section))missing.Add(new { field = "section",       message = "Section is required." });
        if (missing.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = missing });

        var academicYear = req.AcademicYear;
        if (string.IsNullOrWhiteSpace(academicYear))
            academicYear = await _db.Schools.AsNoTracking()
                .Where(x => x.Id == _tenant.SchoolId).Select(x => x.AcademicYear)
                .FirstOrDefaultAsync(ct) ?? "";

        var body = new ClassTeacher
        {
            TeacherUserId = req.TeacherUserId!.Value,
            AcademicYear = academicYear,
            Class = req.Class!.Trim(),
            Section = req.Section!.Trim(),
            Subject = string.IsNullOrWhiteSpace(req.Subject) ? null : req.Subject.Trim(),
            IsPrimary = req.IsPrimary ?? true,
            IsActive = req.IsActive ?? true,
        };

        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.ClassTeachers.Add(body);
        await _db.SaveChangesAsync(ct);
        return StatusCode(201, body);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("teacher:manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.ClassTeachers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { error = "Not found" });
        _db.ClassTeachers.Remove(c);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
