using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
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
    public async Task<IActionResult> Create([FromBody] TimetableWriteRequest req, CancellationToken ct)
    {
        var missing = new List<object>();
        if (string.IsNullOrWhiteSpace(req.Class))   missing.Add(new { field = "class",   message = "Class is required." });
        if (string.IsNullOrWhiteSpace(req.Section)) missing.Add(new { field = "section", message = "Section is required." });
        if (missing.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = missing });

        var academicYear = req.AcademicYear;
        if (string.IsNullOrWhiteSpace(academicYear))
            academicYear = await _db.Schools.AsNoTracking()
                .Where(x => x.Id == _tenant.SchoolId).Select(x => x.AcademicYear)
                .FirstOrDefaultAsync(ct) ?? "";

        var body = new Domain.Entities.Timetable
        {
            Class = req.Class!.Trim(),
            Section = req.Section!.Trim(),
            AcademicYear = academicYear,
            // NOT NULL column; the form may omit it.
            FromDate = req.FromDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            ToDate = req.ToDate,
            Term = string.IsNullOrWhiteSpace(req.Term) ? null : req.Term.Trim(),
        };

        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        _db.Timetables.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.create", "timetable", body.Id.ToString(), ct: ct);
        return StatusCode(201, body);
    }

    [HttpPut("{id:guid}")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TimetableWriteRequest req, CancellationToken ct)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { error = "Not found" });
        if (!string.IsNullOrWhiteSpace(req.Class)) t.Class = req.Class.Trim();
        if (!string.IsNullOrWhiteSpace(req.Section)) t.Section = req.Section.Trim();
        if (!string.IsNullOrWhiteSpace(req.AcademicYear)) t.AcademicYear = req.AcademicYear.Trim();
        if (req.FromDate is { } fd) t.FromDate = fd;
        t.ToDate = req.ToDate;
        t.Term = string.IsNullOrWhiteSpace(req.Term) ? null : req.Term.Trim();
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
    public sealed record CopyDayRequest(short SourceDay, List<short>? TargetDays, bool Replace = true);
    public sealed record CopyFromRequest(Guid SourceTimetableId, bool Replace = true);
    public sealed record CopyToSectionsRequest(List<string>? Sections, bool Replace = true);

    /// <summary>
    /// Copy one day's periods onto other days of the same timetable
    /// (Mon -> Tue/Wed/Thu). Restores POST /:id/copy-day from the Node backend.
    /// </summary>
    [HttpPost("{id:guid}/copy-day")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> CopyDay(Guid id, [FromBody] CopyDayRequest req, CancellationToken ct)
    {
        // Filtering sourceDay out of targetDays is not cosmetic: with
        // replace=true a self-copy would clear the source day first and then
        // have nothing left to copy back.
        var targets = (req.TargetDays ?? []).Where(d => d != req.SourceDay).Distinct().ToList();
        if (targets.Count == 0)
            return BadRequest(new { error = "targetDays must be a non-empty array differing from sourceDay" });

        var tt = await _db.Timetables.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tt is null) return NotFound(new { error = "Timetable not found" });

        var source = tt.Entries.Where(e => e.DayOfWeek == req.SourceDay && e.IsActive).ToList();
        if (source.Count == 0) return BadRequest(new { error = "No entries on source day to copy" });

        var copied = 0;
        foreach (var day in targets)
        {
            if (req.Replace)
            {
                var stale = tt.Entries.Where(e => e.DayOfWeek == day).ToList();
                _db.TimetableEntries.RemoveRange(stale);
                foreach (var e in stale) tt.Entries.Remove(e);
            }

            foreach (var src in source)
            {
                if (!req.Replace && tt.Entries.Any(e => e.DayOfWeek == day && e.SlotNumber == src.SlotNumber))
                    continue;   // merge mode leaves an occupied slot alone
                tt.Entries.Add(Clone(src, tt.Id, day));
                copied++;
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.copy_day", "timetable", id.ToString(), ct: ct);
        return Ok(new { copied, replace = req.Replace, total = tt.Entries.Count });
    }

    /// <summary>
    /// Copy another timetable's entries into this one. Restores
    /// POST /:id/copy-from.
    /// </summary>
    [HttpPost("{id:guid}/copy-from")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> CopyFrom(Guid id, [FromBody] CopyFromRequest req, CancellationToken ct)
    {
        var target = await _db.Timetables.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (target is null) return NotFound(new { error = "Target timetable not found" });
        if (req.SourceTimetableId == id) return BadRequest(new { error = "Source and target are the same timetable" });

        var src = await _db.Timetables.AsNoTracking().Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == req.SourceTimetableId, ct);
        if (src is null) return NotFound(new { error = "Source timetable not found" });

        var incoming = src.Entries.Where(e => e.IsActive).ToList();

        if (req.Replace)
        {
            _db.TimetableEntries.RemoveRange(target.Entries);
            target.Entries.Clear();
            foreach (var e in incoming) target.Entries.Add(Clone(e, target.Id, e.DayOfWeek));
        }
        else
        {
            // Merge by (day, slot): an occupied slot in the target is overwritten
            // by the source, matching the Node behaviour.
            foreach (var e in incoming)
            {
                var existing = target.Entries.FirstOrDefault(
                    x => x.DayOfWeek == e.DayOfWeek && x.SlotNumber == e.SlotNumber);
                if (existing is not null)
                {
                    _db.TimetableEntries.Remove(existing);
                    target.Entries.Remove(existing);
                }
                target.Entries.Add(Clone(e, target.Id, e.DayOfWeek));
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.copy_from", "timetable", id.ToString(), ct: ct);
        return Ok(new { copied = incoming.Count, replace = req.Replace, total = target.Entries.Count });
    }

    /// <summary>
    /// Copy this timetable to other sections of the same class (5-A -> 5-B, 5-C).
    /// Restores POST /:id/copy-to-sections. Reports per-section outcomes rather
    /// than failing the whole call when one section has no timetable yet.
    /// </summary>
    [HttpPost("{id:guid}/copy-to-sections")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> CopyToSections(Guid id, [FromBody] CopyToSectionsRequest req, CancellationToken ct)
    {
        var sections = (req.Sections ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (sections.Count == 0) return BadRequest(new { error = "sections array required" });

        var src = await _db.Timetables.AsNoTracking().Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (src is null) return NotFound(new { error = "Source timetable not found" });

        var incoming = src.Entries.Where(e => e.IsActive).ToList();
        var results = new List<object>();

        foreach (var section in sections)
        {
            if (string.Equals(section, src.Section, StringComparison.OrdinalIgnoreCase))
            { results.Add(new { section, status = "skipped", reason = "same as source" }); continue; }

            var target = await _db.Timetables.Include(x => x.Entries).FirstOrDefaultAsync(
                x => x.Class == src.Class && x.Section == section
                  && x.AcademicYear == src.AcademicYear && x.Term == src.Term, ct);
            if (target is null)
            { results.Add(new { section, status = "no_target", reason = "no timetable exists for this section yet" }); continue; }

            if (req.Replace)
            {
                _db.TimetableEntries.RemoveRange(target.Entries);
                target.Entries.Clear();
                foreach (var e in incoming) target.Entries.Add(Clone(e, target.Id, e.DayOfWeek));
            }
            else
            {
                foreach (var e in incoming)
                {
                    if (target.Entries.Any(x => x.DayOfWeek == e.DayOfWeek && x.SlotNumber == e.SlotNumber)) continue;
                    target.Entries.Add(Clone(e, target.Id, e.DayOfWeek));
                }
            }
            results.Add(new { section, status = "copied", copied = incoming.Count });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("timetable.copy_to_sections", "timetable", id.ToString(), ct: ct);
        return Ok(new { results });
    }

    /// <summary>
    /// A fresh row from an existing one. Id stays Guid.Empty so the database
    /// assigns it — carrying the source Id over would put the same key in the
    /// change tracker twice and 500 the save.
    /// </summary>
    private static TimetableEntry Clone(TimetableEntry e, Guid timetableId, short dayOfWeek) => new()
    {
        Id = Guid.Empty,
        TimetableId = timetableId,
        DayOfWeek = dayOfWeek,
        SlotNumber = e.SlotNumber,
        SubjectId = e.SubjectId,
        SubjectName = e.SubjectName,
        TeacherId = e.TeacherId,
        TeacherName = e.TeacherName,
        Room = e.Room,
        Notes = e.Notes,
        IsActive = true,
    };

    [HttpPost("{id:guid}/entries")]
    [RequirePrivilege("timetable:manage")]
    public async Task<IActionResult> SaveEntries(Guid id, [FromBody] SaveEntriesRequest req, CancellationToken ct)
    {
        var t = await _db.Timetables.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { error = "Not found" });

        // This endpoint REPLACES the whole entry set. The old code did:
        //     RemoveRange(t.Entries); t.Entries = req.Entries; SaveChanges();
        // which blows up with a 500 whenever an incoming entry carries an Id
        // that is already tracked (e.g. a client that copies one day's periods
        // onto another by spreading the existing objects). EF then has the same
        // key marked both Deleted and Added in one unit of work and refuses to
        // track it. Duplicate Ids *within* the payload fail the same way.
        //
        // The incoming rows are always new rows as far as the database is
        // concerned, so ignore any client-supplied Id and let the store assign
        // one. Also normalise the FK/back-reference so EF does not try to infer
        // them from a partially-populated graph.
        _db.TimetableEntries.RemoveRange(t.Entries);

        var incoming = req.Entries ?? [];
        foreach (var e in incoming)
        {
            e.Id = Guid.Empty;          // let the database generate it
            e.TimetableId = t.Id;
            e.Timetable = null!;
        }

        t.Entries = incoming;
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
