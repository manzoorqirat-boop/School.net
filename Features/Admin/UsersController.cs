using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Admin;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public UsersController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    [RequirePrivilege("user:view")]
    public async Task<IActionResult> List([FromQuery] string? role, [FromQuery] string? q,
        [FromQuery] int? page, [FromQuery] int? limit, CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(role) && Enum.TryParse<UserRole>(role, true, out var r)) query = query.Where(u => u.Role == r);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q}%";
            query = query.Where(u => EF.Functions.ILike(u.Name, pat) || EF.Functions.ILike(u.Username, pat));
        }
        var p = Math.Max(1, page ?? 1); var l = Math.Clamp(limit ?? PageInfo.DefaultLimit, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(u => u.Name).Skip((p - 1) * l).Take(l).ToListAsync(ct);
        return Ok(new Paged<User> { Items = items, Pagination = PageInfo.Create(total, p, l) });
    }

    [HttpGet("{id:guid}")]
    [RequirePrivilege("user:view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? NotFound(new { error = "Not found" }) : Ok(u);
    }

    public sealed record CreateUserRequest(string Username, string Password, string Name, string Role,
        string? Email, string? Phone, List<Guid>? ParentOf);

    [HttpPost]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var check = PasswordPolicy.Validate(req.Password, req.Username);
        if (!check.Ok) return BadRequest(new { error = check.Error, code = check.Code });
        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            return BadRequest(new { error = $"Invalid role '{req.Role}'" });

        var user = new User
        {
            SchoolId = _tenant.SchoolId, SchoolSlug = null,
            Username = req.Username.ToLowerInvariant(),
            Password = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 10),
            Name = req.Name, Role = role, Email = req.Email, Phone = req.Phone, IsActive = true,
        };
        if (req.ParentOf is { Count: > 0 })
        {
            var kids = await _db.Students.Where(s => req.ParentOf.Contains(s.Id)).ToListAsync(ct);
            foreach (var k in kids) user.ParentOf.Add(k);
        }
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("user.create", "user", user.Id.ToString(), ct: ct);
        return StatusCode(201, user);
    }

    public sealed record UpdateUserRequest(string? Name, string? Email, string? Phone, bool? IsActive, List<Guid>? ParentOf);
    [HttpPut("{id:guid}")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var u = await _db.Users.Include(x => x.ParentOf).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound(new { error = "Not found" });
        if (req.Name is not null) u.Name = req.Name;
        if (req.Email is not null) u.Email = req.Email;
        if (req.Phone is not null) u.Phone = req.Phone;
        if (req.IsActive is { } a) u.IsActive = a;
        if (req.ParentOf is not null)
        {
            u.ParentOf.Clear();
            var kids = await _db.Students.Where(s => req.ParentOf.Contains(s.Id)).ToListAsync(ct);
            foreach (var k in kids) u.ParentOf.Add(k);
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("user.update", "user", id.ToString(), ct: ct);
        return Ok(u);
    }

    public sealed record ResetPasswordRequest(string NewPassword);
    [HttpPost("{id:guid}/reset-password")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var u = await _db.Users.Include(x => x.RefreshTokens).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound(new { error = "Not found" });
        var check = PasswordPolicy.Validate(req.NewPassword, u.Username);
        if (!check.Ok) return BadRequest(new { error = check.Error, code = check.Code });
        u.Password = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 10);
        u.PasswordChangedAt = DateTime.UtcNow;
        u.RefreshTokens.Clear();                          // force re-login everywhere
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("user.reset_password", "user", id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("user:manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound(new { error = "Not found" });
        u.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("user.deactivate", "user", id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }
}

[ApiController]
[Route("api/schools")]
public sealed class SchoolsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public SchoolsController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    // Own school (any authenticated user in a tenant).
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        if (_tenant.SchoolId is not { } sid) return Ok(new { school = (object?)null });
        var s = await _db.Schools.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == sid, ct);
        return Ok(new { school = s });
    }

    [HttpGet("{id:guid}")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // Only superadmin (unfiltered) or a member of that school can read it.
        if (!_tenant.IsSuperAdmin && _tenant.SchoolId != id) return StatusCode(403, new { error = "Forbidden" });
        var s = await _db.Schools.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? NotFound(new { error = "Not found" }) : Ok(s);
    }

    [HttpPost]
    [RequirePrivilege("school:manage")]
    public async Task<IActionResult> Create([FromBody] School body, CancellationToken ct)
    {
        body.Slug = body.Slug.ToLowerInvariant();
        _db.Schools.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("school.create", "school", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("school:settings")]
    public async Task<IActionResult> Update(Guid id, [FromBody] School body, CancellationToken ct)
    {
        if (!_tenant.IsSuperAdmin && _tenant.SchoolId != id) return StatusCode(403, new { error = "Forbidden" });
        var s = await _db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        s.Name = body.Name; s.Email = body.Email; s.Phone = body.Phone;
        s.City = body.City; s.State = body.State; s.Pincode = body.Pincode;
        s.AcademicYear = body.AcademicYear; s.PrimaryColor = body.PrimaryColor;
        s.Classes = body.Classes; s.Sections = body.Sections; s.WorkingDays = body.WorkingDays;
        s.FeeBillingDay = body.FeeBillingDay; s.FeeReminderDay = body.FeeReminderDay;
        s.LeaveRequireApproval = body.LeaveRequireApproval;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("school.update", "school", id.ToString(), ct: ct);
        return Ok(s);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("school:manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var s = await _db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        s.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("school.deactivate", "school", id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }
}
