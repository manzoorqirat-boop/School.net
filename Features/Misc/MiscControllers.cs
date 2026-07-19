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

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        // Targeted to the caller's role, or created by admin roles (who see all).
        var role = _tenant.Role ?? "";
        var q = _db.Polls.AsNoTracking().Include(p => p.Questions).ThenInclude(x => x.Options).AsQueryable();
        if (role is not ("school_admin" or "principal" or "superadmin"))
            q = q.Where(p => p.TargetRoles.Contains(role) && p.Status == PollStatus.Active);
        return Ok(new { items = await q.OrderByDescending(p => p.CreatedAt).ToListAsync(ct) });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await _db.Polls.AsNoTracking().Include(x => x.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? NotFound(new { error = "Not found" }) : Ok(p);
    }

    [HttpPost]
    // Node gated poll writes on authenticate only (no privilege) — match that.
    public async Task<IActionResult> Create([FromBody] Poll body, CancellationToken ct)
    {
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
    public async Task<IActionResult> Update(Guid id, [FromBody] Poll body, CancellationToken ct)
    {
        var p = await _db.Polls.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });
        p.Title = body.Title; p.Description = body.Description; p.Category = body.Category;
        p.TargetRoles = body.TargetRoles; p.Status = body.Status;
        p.StartDate = body.StartDate; p.EndDate = body.EndDate;
        p.ShowResultsBeforeClose = body.ShowResultsBeforeClose; p.AllowAnonymous = body.AllowAnonymous;
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
        if (_tenant.SchoolId is not { } sid) return Ok(new { matrix = new Dictionary<string, object>() });
        var m = await _resolver.MatrixAsync(sid);
        var matrix = m.ToDictionary(kv => kv.Key, kv => (object)new { roles = kv.Value.Roles, isCustomized = kv.Value.IsCustomized });
        return Ok(new { matrix });
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
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? page, [FromQuery] int? limit, CancellationToken ct)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(entity)) q = q.Where(a => a.Entity == entity);
        if (from is { } f) q = q.Where(a => a.CreatedAt >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(a => a.CreatedAt <= t.ToDateTime(TimeOnly.MaxValue));
        var p = Math.Max(1, page ?? 1); var l = Math.Clamp(limit ?? PageInfo.DefaultLimit, 1, 100);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.CreatedAt).Skip((p - 1) * l).Take(l).ToListAsync(ct);
        return Ok(new Paged<AuditLog> { Items = items, Pagination = PageInfo.Create(total, p, l) });
    }

    [HttpGet("actions")]
    [RequirePrivilege("audit:view")]
    public async Task<IActionResult> Actions(CancellationToken ct) =>
        Ok(new { actions = await _db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct) });
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
    public async Task<IActionResult> Create([FromBody] ClassTeacher body, CancellationToken ct)
    {
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
