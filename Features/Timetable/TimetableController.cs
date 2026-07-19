using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Timetable;

[ApiController]
[Route("api/timetables")]
public sealed class TimetableController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public TimetableController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    [RequirePrivilege("timetable:view")]
    public async Task<IActionResult> Get([FromQuery] string? @class, [FromQuery] string? section,
        [FromQuery] string? academicYear, CancellationToken ct)
    {
        var q = _db.Timetables.AsNoTracking().Include(t => t.Entries).AsQueryable();
        if (!string.IsNullOrEmpty(@class)) q = q.Where(t => t.Class == @class);
        if (!string.IsNullOrEmpty(section)) q = q.Where(t => t.Section == section);
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(t => t.AcademicYear == academicYear);
        return Ok(new { items = await q.OrderByDescending(t => t.FromDate).ToListAsync(ct) });
    }

    [HttpPost]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Create([FromBody] Domain.Entities.Timetable body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.Timetables.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.create", "timetable", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Domain.Entities.Timetable body, CancellationToken ct)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { error = "Not found" });
        t.Class = body.Class; t.Section = body.Section; t.AcademicYear = body.AcademicYear;
        t.FromDate = body.FromDate; t.ToDate = body.ToDate; t.Term = body.Term;
        await _db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { error = "Not found" });
        _db.Timetables.Remove(t);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    public sealed record SaveEntriesRequest(List<TimetableEntry>? Entries);
    [HttpPost("{id:guid}/entries")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> SaveEntries(Guid id, [FromBody] SaveEntriesRequest req, CancellationToken ct)
    {
        var t = await _db.Timetables.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { error = "Not found" });
        _db.TimetableEntries.RemoveRange(t.Entries);
        t.Entries = req.Entries ?? [];
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.save_entries", "timetable", id.ToString(), ct: ct);
        return Ok(t);
    }

    [HttpDelete("{id:guid}/entries/{entryId:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> DeleteEntry(Guid id, Guid entryId, CancellationToken ct)
    {
        var e = await _db.TimetableEntries.FirstOrDefaultAsync(x => x.Id == entryId && x.TimetableId == id, ct);
        if (e is null) return NotFound(new { error = "Not found" });
        _db.TimetableEntries.Remove(e);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/publish")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { error = "Not found" });
        t.Status = TimetableStatus.Active;
        await _db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpGet("{id:guid}/variations")]
    [RequirePrivilege("timetable:view")]
    public async Task<IActionResult> GetVariations(Guid id, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var q = _db.TimetableVariations.AsNoTracking().Where(v => v.TimetableId == id);
        if (date is { } d) q = q.Where(v => v.Date == d);
        return Ok(new { items = await q.ToListAsync(ct) });
    }

    [HttpPost("{id:guid}/variations")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> AddVariation(Guid id, [FromBody] TimetableVariation body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty; body.TimetableId = id;
        body.CreatedByUserId = _tenant.UserId;
        _db.TimetableVariations.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.add_variation", "timetable", id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpDelete("{id:guid}/variations/{varId:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> DeleteVariation(Guid id, Guid varId, CancellationToken ct)
    {
        var v = await _db.TimetableVariations.FirstOrDefaultAsync(x => x.Id == varId && x.TimetableId == id, ct);
        if (v is null) return NotFound(new { error = "Not found" });
        _db.TimetableVariations.Remove(v);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpGet("teacher/{teacherId:guid}")]
    [RequirePrivilege("timetable:view")]
    public async Task<IActionResult> TeacherWorkload(Guid teacherId, CancellationToken ct)
    {
        var entries = await _db.TimetableEntries.AsNoTracking()
            .Where(e => e.TeacherId == teacherId && e.IsActive)
            .OrderBy(e => e.DayOfWeek).ThenBy(e => e.SlotNumber).ToListAsync(ct);
        return Ok(new { teacherId, entries, count = entries.Count });
    }
}

[ApiController]
[Route("api/time-slots")]
public sealed class TimeSlotsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    public TimeSlotsController(AppDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

    [HttpGet]
    [RequirePrivilege("timetable:view")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new { items = await _db.TimeSlots.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SlotNumber).ToListAsync(ct) });

    [HttpPost]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Create([FromBody] TimeSlot body, CancellationToken ct)
    {
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        body.ComputeDurations();
        _db.TimeSlots.Add(body);
        await _db.SaveChangesAsync(ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TimeSlot body, CancellationToken ct)
    {
        var s = await _db.TimeSlots.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        s.Name = body.Name; s.SlotNumber = body.SlotNumber;
        s.DefaultStartTime = body.DefaultStartTime; s.DefaultEndTime = body.DefaultEndTime;
        s.DayTimes = body.DayTimes; s.SpansMultiplePeriods = body.SpansMultiplePeriods;
        s.NextSlotNumber = body.NextSlotNumber; s.IsActive = body.IsActive;
        s.ComputeDurations();
        await _db.SaveChangesAsync(ct);
        return Ok(s);
    }

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await _db.TimeSlots.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });
        _db.TimeSlots.Remove(s);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
