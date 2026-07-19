using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Fees;

[ApiController]
[Route("api/fee-structures")]
public sealed class FeeStructuresController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public FeeStructuresController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> List([FromQuery] string? academicYear, [FromQuery] string? @class,
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var q = _db.FeeStructures.AsNoTracking().Include(f => f.Heads).Include(f => f.Installments).AsQueryable();
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(f => f.AcademicYear == academicYear);
        if (!string.IsNullOrEmpty(@class)) q = q.Where(f => f.Class == @class);
        if (!includeInactive) q = q.Where(f => f.IsActive);
        return Ok(new { items = await q.ToListAsync(ct) });
    }

    [HttpGet("{id:guid}")]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var f = await _db.FeeStructures.Include(x => x.Heads).Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return f is null ? NotFound(new { error = "Not found" }) : Ok(f);
    }

    [HttpPost]
    [RequirePrivilege("fee:create")]
    public async Task<IActionResult> Create([FromBody] FeeStructure body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        body.CreatedByUserId = _tenant.UserId;
        body.CreatedByUsername = User.Identity?.Name;
        body.Validate();                                  // ≥1 head, installments sum 100, unique names
        _db.FeeStructures.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("fee_structure.create", "fee_structure", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("fee:manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FeeStructure body, CancellationToken ct)
    {
        var f = await _db.FeeStructures.Include(x => x.Heads).Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound(new { error = "Not found" });

        f.Name = body.Name; f.Class = body.Class; f.Section = body.Section;
        f.AcademicYear = body.AcademicYear; f.Currency = body.Currency;
        _db.FeeHeads.RemoveRange(f.Heads); f.Heads = body.Heads;
        _db.FeeInstallments.RemoveRange(f.Installments); f.Installments = body.Installments;
        f.Validate();
        f.RecordEdit(_tenant.UserId, User.Identity?.Name, "Structure updated");
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("fee_structure.update", "fee_structure", id.ToString(), ct: ct);
        return Ok(f);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("fee:manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var f = await _db.FeeStructures.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound(new { error = "Not found" });
        f.IsActive = false;                               // soft-retire, not delete
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("fee_structure.deactivate", "fee_structure", id.ToString(), ct: ct);
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/reactivate")]
    [RequirePrivilege("fee:manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var f = await _db.FeeStructures.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound(new { error = "Not found" });
        f.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/clone")]
    [RequirePrivilege("fee:manage")]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneRequest req, CancellationToken ct)
    {
        var src = await _db.FeeStructures.AsNoTracking().Include(x => x.Heads).Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (src is null) return NotFound(new { error = "Not found" });

        var copy = new FeeStructure
        {
            SchoolId = _tenant.SchoolId ?? Guid.Empty,
            Name = req.Name ?? $"{src.Name} (copy)",
            AcademicYear = req.AcademicYear ?? src.AcademicYear,
            Class = req.Class ?? src.Class, Section = src.Section, Currency = src.Currency,
            CreatedByUserId = _tenant.UserId, CreatedByUsername = User.Identity?.Name,
            Heads = src.Heads.Select(h => new FeeHead { Name = h.Name, Amount = h.Amount, Frequency = h.Frequency, IsOptional = h.IsOptional, Description = h.Description }).ToList(),
            Installments = src.Installments.Select(i => new FeeInstallment { Name = i.Name, DueDate = i.DueDate, Percentage = i.Percentage }).ToList(),
        };
        copy.Validate();
        _db.FeeStructures.Add(copy);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("fee_structure.clone", "fee_structure", copy.Id.ToString(), ct: ct);
        return StatusCode(201, copy);
    }

    public sealed record CloneRequest(string? Name, string? AcademicYear, string? Class);
}
