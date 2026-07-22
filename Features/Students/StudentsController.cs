using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Features.Students;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Students;

[ApiController]
[Route("api/students")]
public sealed class StudentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly ParentLinkService _parents;

    public StudentsController(AppDbContext db, ITenantContext tenant,
                              IAuditWriter audit, ParentLinkService parents)
    {
        _db = db; _tenant = tenant; _audit = audit; _parents = parents;
    }

    // ── GET /api/students/public/:token  (UNAUTHENTICATED) ────────────────

    [HttpGet("public/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Public(string token, CancellationToken ct)
    {
        // Unauthenticated: bypass the tenant filter (no tenant yet), match on
        // the indexed share token + enabled flag only.
        var s = await _db.Students
            .IgnoreQueryFilters()
            .Include(x => x.School)
            .FirstOrDefaultAsync(x =>
                x.ShareToken == token && x.ShareEnabled && !x.IsDeleted, ct);

        if (s is null) return NotFound(new { error = "Link invalid or disabled" });

        // Explicit projection — the public card must not leak the full row
        // (Aadhaar, docs, addresses). Verbatim field set from Node.
        return Ok(new
        {
            schoolName = s.School?.Name,
            academicYear = s.AcademicYear,
            admissionNo = s.AdmissionNo,
            firstName = s.FirstName,
            lastName = s.LastName,
            @class = s.Class,
            section = s.Section,
            rollNo = s.RollNo,
            dob = s.Dob,
            gender = s.Gender,
            bloodGroup = s.BloodGroup,
            house = s.House,
            fatherName = s.FatherName,
            motherName = s.MotherName,
            status = s.Status,
        });
    }

    // ── GET /api/students ─────────────────────────────────────────────────

    [HttpGet]
    [RequirePrivilege("student:view")]
    public async Task<IActionResult> List(
        [FromQuery] string? @class, [FromQuery] string? section,
        [FromQuery] string? status, [FromQuery] string? category,
        [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? limit,
        CancellationToken ct)
    {
        var query = _db.Students.AsNoTracking().AsQueryable();

        // Row-scoping: parent/student see only theirs; empty page (not 403) when
        // they have no linkage.
        query = RowScope.ApplyStudentScope(query, _tenant, await ParentOfIdsAsync(ct));

        if (!string.IsNullOrEmpty(@class)) query = query.Where(s => s.Class == @class);
        if (!string.IsNullOrEmpty(section)) query = query.Where(s => s.Section == section);
        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<StudentStatus>(status, true, out var st))
            query = query.Where(s => s.Status == st);
        if (!string.IsNullOrEmpty(category) &&
            Enum.TryParse<StudentCategory>(category, true, out var cat))
            query = query.Where(s => s.Category == cat);

        // Search: Mongo ran a 9-field case-insensitive regex; here ILIKE, which
        // the pg_trgm GIN indexes accelerate on the name/admissionNo columns.
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = $"%{q}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.FirstName, pat) ||
                EF.Functions.ILike(s.LastName ?? "", pat) ||
                EF.Functions.ILike(s.FirstNameHi ?? "", pat) ||
                EF.Functions.ILike(s.LastNameHi ?? "", pat) ||
                EF.Functions.ILike(s.AdmissionNo, pat) ||
                EF.Functions.ILike(s.FatherName ?? "", pat) ||
                EF.Functions.ILike(s.Phone ?? "", pat) ||
                EF.Functions.ILike(s.FatherPhone ?? "", pat) ||
                EF.Functions.ILike(s.MotherPhone ?? "", pat));
        }

        var p = Math.Max(1, page ?? 1);
        var l = Math.Clamp(limit ?? PageInfo.DefaultLimit, 1, 100);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(s => s.Class).ThenBy(s => s.Section).ThenBy(s => s.RollNo)
            .Skip((p - 1) * l).Take(l)
            .ToListAsync(ct);

        return Ok(new Paged<Student>
        {
            Items = items,
            Pagination = PageInfo.Create(total, p, l),
        });
    }

    // ── GET /api/students/next-admission-no ───────────────────────────────

    [HttpGet("next-admission-no")]
    [RequirePrivilege("student:view")]
    public async Task<IActionResult> NextAdmissionNo(CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = year.ToString();

        var nums = await _db.Students.AsNoTracking()
            .Where(s => s.AdmissionNo.StartsWith(prefix))
            .Select(s => s.AdmissionNo)
            .ToListAsync(ct);

        var max = nums
            .Select(a => int.TryParse(a.Length > 4 ? a[4..] : "", out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();

        return Ok(new { admissionNo = $"{year}{(max + 1):D4}" });
    }

    // ── GET /api/students/next-roll-no ────────────────────────────────────

    [HttpGet("next-roll-no")]
    [RequirePrivilege("student:view")]
    public async Task<IActionResult> NextRollNo(
        [FromQuery] string? @class, [FromQuery] string? section, CancellationToken ct)
    {
        var cls = (@class ?? "").Trim();
        var sec = (section ?? "").Trim();
        if (string.IsNullOrEmpty(cls) || string.IsNullOrEmpty(sec))
            return BadRequest(new { error = "class and section required" });

        var prefix = sec.ToUpperInvariant();

        var rolls = await _db.Students.AsNoTracking()
            .Where(s => s.Class == cls && s.Section == sec && s.Status != StudentStatus.Inactive)
            .Select(s => s.RollNo)
            .ToListAsync(ct);

        var rx = new System.Text.RegularExpressions.Regex($"^{prefix}(\\d+)$");
        var max = rolls
            .Select(r => rx.Match((r ?? "").ToUpperInvariant()))
            .Where(m => m.Success)
            .Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();

        return Ok(new { rollNo = $"{prefix}{(max + 1):D3}" });
    }

    // ── GET /api/students/:id ─────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    [RequirePrivilege("student:view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var s = await _db.Students
            .Include(x => x.Siblings).Include(x => x.PassedExams)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (s is null) return NotFound(new { error = "Not found" });

        // In-tenant but out of row-scope ⇒ 403 (distinct from the 404 above).
        if (!RowScope.CanAccessStudent(s, _tenant, await ParentOfIdsAsync(ct)))
            return StatusCode(403, new { error = "Forbidden" });

        return Ok(s);
    }

    // ── POST /api/students ────────────────────────────────────────────────

    [HttpPost]
    [RequirePrivilege("student:create")]
    public async Task<IActionResult> Create([FromBody] StudentWriteRequest req, CancellationToken ct)
    {
        // Required fields are checked HERE, not by the model binder, so failures
        // come back in the { error, code, details[] } envelope the app parses.
        var missing = new List<object>();
        if (string.IsNullOrWhiteSpace(req.FirstName))   missing.Add(new { field = "firstName",   message = "First name is required." });
        if (string.IsNullOrWhiteSpace(req.AdmissionNo)) missing.Add(new { field = "admissionNo", message = "Admission number is required." });
        if (string.IsNullOrWhiteSpace(req.Class))       missing.Add(new { field = "class",       message = "Class is required." });
        if (string.IsNullOrWhiteSpace(req.Section))     missing.Add(new { field = "section",     message = "Section is required." });
        if (missing.Count > 0)
            return BadRequest(new { error = "Validation failed", code = ErrorCodes.ValidationError, details = missing });

        var body = new Student();
        ApplyWrite(body, req, isCreate: true);

        // Tenant stamped by SaveChanges; academicYear resolved from school default.
        body.SchoolId = _tenant.SchoolId ?? Guid.Empty;
        body.AcademicYear = await ResolveAcademicYearAsync(req.AcademicYear, ct);

        // NOT NULL in the schema; the form may legitimately omit it.
        body.AdmissionDate = req.AdmissionDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        _db.Students.Add(body);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("student.create", "student", body.Id.ToString(), ct: ct);

        // Auto parent create/link — best-effort, never blocks the 201.
        var school = await _db.Schools.AsNoTracking()
            .Where(s => s.Id == body.SchoolId).Select(s => s.Slug).FirstOrDefaultAsync(ct);

        var src = ParentLinkService.PickParentSource(
            body.GuardianName, body.GuardianPhone,
            body.FatherName, body.FatherPhone,
            body.MotherName, body.MotherPhone);

        var link = await _parents.CreateOrLinkAsync(body, body.SchoolId, school, src, ct);

        if (link.IsNew)
            await _audit.WriteAsync("parent.auto_create", "user", link.UserId?.ToString(), ct: ct);
        else if (link.Linked)
            await _audit.WriteAsync("parent.auto_link", "user", link.UserId?.ToString(), ct: ct);

        // _parent block: password shown ONCE to the admin. Shape verbatim.
        object? parentInfo = link switch
        {
            { IsNew: true } => new { created = true, username = link.Username, password = link.PlaintextPassword, source = link.Source },
            { Linked: true } => new { created = false, linked = true, parentName = link.ParentName },
            { Skipped: true } => new { created = false, skipped = true, reason = link.Reason },
            { Error: not null } => new { created = false, error = link.Error },
            _ => null,
        };

        return StatusCode(201, MergeParent(body, parentInfo));
    }

    // ── PUT /api/students/:id ─────────────────────────────────────────────

    [HttpPut("{id:guid}")]
    [RequirePrivilege("student:update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] StudentWriteRequest req, CancellationToken ct)
    {
        var s = await _db.Students
            .Include(x => x.Siblings).Include(x => x.PassedExams)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });

        // Copy mutable fields; blank academicYear is ignored (Node deletes it).
        ApplyWrite(s, req, isCreate: false);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("student.update", "student", s.Id.ToString(), ct: ct);

        return Ok(s);
    }

    // ── DELETE /api/students/:id  (soft — status=inactive) ────────────────

    [HttpDelete("{id:guid}")]
    [RequirePrivilege("student:delete")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var s = await _db.Students.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });

        // Node sets status=inactive here (NOT the isDeleted soft-delete flag) —
        // students stay listable-by-filter and their admission number stays
        // reserved. Faithful.
        s.Status = StudentStatus.Inactive;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("student.deactivate", "student", s.Id.ToString(), ct: ct);

        return Ok(new { ok = true });
    }

    // ── POST/DELETE /api/students/:id/share ───────────────────────────────

    [HttpPost("{id:guid}/share")]
    [RequirePrivilege("student:update")]
    public async Task<IActionResult> EnableShare(Guid id, CancellationToken ct)
    {
        var s = await _db.Students.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });

        var token = s.EnableShare();     // entity method: 16-byte hex, sets flag
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("student.share.enable", "student", s.Id.ToString(), ct: ct);

        return Ok(new { token, shareEnabled = true });
    }

    [HttpDelete("{id:guid}/share")]
    [RequirePrivilege("student:update")]
    public async Task<IActionResult> DisableShare(Guid id, CancellationToken ct)
    {
        var s = await _db.Students.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound(new { error = "Not found" });

        s.DisableShare();
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("student.share.disable", "student", s.Id.ToString(), ct: ct);

        return Ok(new { shareEnabled = false });
    }

    // ── POST /api/students/bulk-import ────────────────────────────────────

    public sealed record BulkImportRequest(List<StudentWriteRequest>? Students);

    [HttpPost("bulk-import")]
    [RequirePrivilege("student:create")]
    public async Task<IActionResult> BulkImport([FromBody] BulkImportRequest req, CancellationToken ct)
    {
        var rows = req.Students;
        if (rows is null || rows.Count == 0)
            return BadRequest(new { error = "students array required" });
        if (rows.Count > 1000)
            return BadRequest(new { error = "Max 1000 per import" });

        var defaultYear = await ResolveAcademicYearAsync(null, ct);
        var errors = new List<object>();
        int created = 0, skipped = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var src = rows[i];
            if (string.IsNullOrEmpty(src.AdmissionNo) || string.IsNullOrEmpty(src.FirstName) ||
                string.IsNullOrEmpty(src.Class) || string.IsNullOrEmpty(src.Section))
            {
                errors.Add(new { row = i + 1, error = "Missing required fields" }); skipped++; continue;
            }

            var dup = await _db.Students.AsNoTracking()
                .AnyAsync(s => s.AdmissionNo == src.AdmissionNo, ct);
            if (dup)
            {
                errors.Add(new { row = i + 1, admissionNo = src.AdmissionNo, error = "Duplicate admission no" });
                skipped++; continue;
            }

            var row = new Student();
            ApplyWrite(row, src, isCreate: true);
            row.SchoolId = _tenant.SchoolId ?? Guid.Empty;
            if (string.IsNullOrEmpty(row.AcademicYear)) row.AcademicYear = defaultYear;
            row.AdmissionDate = src.AdmissionDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

            _db.Students.Add(row);
            try { await _db.SaveChangesAsync(ct); created++; }
            catch (Exception e) { _db.Entry(row).State = EntityState.Detached; errors.Add(new { row = i + 1, error = e.Message }); skipped++; }
        }

        await _audit.WriteAsync("student.bulk_import", "student",
            metaJson: System.Text.Json.JsonSerializer.Serialize(new { created, skipped }), ct: ct);

        return Ok(new { created, skipped, errors });
    }

    // ── POST /api/students/promote  (single transaction) ──────────────────

    public sealed record PromoteEntry(string? FromClass, string? FromSection, string? ToClass, string? ToSection);
    public sealed record PromoteRequest(
        string? FromAcademicYear, string? ToAcademicYear,
        List<PromoteEntry>? Promotions, List<string>? GraduatingClasses);

    [HttpPost("promote")]
    [RequirePrivilege("student:update")]
    public async Task<IActionResult> Promote([FromBody] PromoteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.FromAcademicYear) || string.IsNullOrEmpty(req.ToAcademicYear))
            return BadRequest(new { error = "fromAcademicYear and toAcademicYear are required" });

        var promotions = req.Promotions ?? [];
        var graduating = req.GraduatingClasses ?? [];
        if (promotions.Count == 0 && graduating.Count == 0)
            return BadRequest(new { error = "Provide at least one entry in promotions or graduatingClasses" });

        var results = new List<object>();
        var totalPromoted = 0;

        // All moves in ONE transaction — a half-promoted year is corruption.
        await Tx.RunAsync(_db, async () =>
        {
            foreach (var cls in graduating)
            {
                var n = await _db.Students
                    .Where(s => s.Class == cls && s.AcademicYear == req.FromAcademicYear
                             && s.Status == StudentStatus.Active)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, StudentStatus.Graduated)
                        .SetProperty(s => s.AcademicYear, req.ToAcademicYear), ct);
                results.Add(new { fromClass = cls, toClass = "(graduated)", modifiedCount = n });
                totalPromoted += n;
            }

            foreach (var p in promotions)
            {
                if (string.IsNullOrEmpty(p.FromClass) || string.IsNullOrEmpty(p.ToClass)) continue;
                var toSection = string.IsNullOrEmpty(p.ToSection) ? p.FromSection : p.ToSection;

                var q = _db.Students.Where(s =>
                    s.Class == p.FromClass && s.AcademicYear == req.FromAcademicYear
                    && s.Status == StudentStatus.Active);
                if (!string.IsNullOrEmpty(p.FromSection))
                    q = q.Where(s => s.Section == p.FromSection);

                var n = toSection is null
                    ? await q.ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Class, p.ToClass)
                        .SetProperty(s => s.AcademicYear, req.ToAcademicYear), ct)
                    : await q.ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Class, p.ToClass)
                        .SetProperty(s => s.Section, toSection)
                        .SetProperty(s => s.AcademicYear, req.ToAcademicYear), ct);

                results.Add(new { p.FromClass, p.FromSection, p.ToClass, toSection, modifiedCount = n });
                totalPromoted += n;
            }
        }, ct);   // → exception middleware envelope on failure

        await _audit.WriteAsync("student.bulk_promote", "student",
            metaJson: System.Text.Json.JsonSerializer.Serialize(new { req.FromAcademicYear, req.ToAcademicYear, totalPromoted }),
            ct: ct);

        return Ok(new { ok = true, totalPromoted, results });
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private async Task<IReadOnlyCollection<Guid>> ParentOfIdsAsync(CancellationToken ct)
    {
        if (_tenant.Role != "parent" || _tenant.UserId is not { } uid)
            return Array.Empty<Guid>();

        return await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == uid)
            .SelectMany(u => u.ParentOf.Select(s => s.Id))
            .ToListAsync(ct);
    }

    private async Task<string> ResolveAcademicYearAsync(string? bodyValue, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(bodyValue)) return bodyValue.Trim();

        var ay = await _db.Schools.AsNoTracking()
            .Where(s => s.Id == _tenant.SchoolId)
            .Select(s => s.AcademicYear)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(ay)) return ay;

        var y = DateTime.UtcNow.Year;
        return $"{y}-{y + 1}";
    }

    private static object MergeParent(Student s, object? parentInfo)
    {
        // Serialize the student then attach _parent — matches Node's
        // { ...s.toObject(), _parent }.
        var json = System.Text.Json.JsonSerializer.SerializeToNode(s)!.AsObject();
        json["_parent"] = parentInfo is null
            ? null
            : System.Text.Json.JsonSerializer.SerializeToNode(parentInfo);
        return json;
    }

    /// <summary>
    /// Copies client-supplied fields from the DTO onto the tracked entity.
    ///
    /// Semantics preserved from the previous ApplyUpdate:
    ///   • blank academicYear is ignored (Node deleted it from the $set)
    ///   • child collections are replace-all ONLY when the client sent the
    ///     array; null = "not editing these", empty = "clear them"
    ///   • system fields (share token, isDeleted, timestamps, schoolId, _id)
    ///     are never written here — they have no DTO property at all now.
    ///
    /// On create, `isCreate` lets admissionNo through; it is immutable
    /// afterwards (it is the reserved key a deactivated student keeps).
    /// </summary>
    private void ApplyWrite(Student s, StudentWriteRequest b, bool isCreate)
    {
        if (isCreate && b.AdmissionNo is not null) s.AdmissionNo = b.AdmissionNo.Trim();

        if (b.FirstName is not null) s.FirstName = b.FirstName.Trim();
        if (b.Class is not null) s.Class = b.Class.Trim();
        if (b.Section is not null) s.Section = b.Section.Trim();

        s.RollNo = b.RollNo; s.LastName = b.LastName;
        s.FirstNameHi = b.FirstNameHi; s.LastNameHi = b.LastNameHi;
        s.Dob = b.Dob; s.Gender = b.Gender; s.BloodGroup = b.BloodGroup;

        if (!string.IsNullOrWhiteSpace(b.AcademicYear)) s.AcademicYear = b.AcademicYear;
        if (!isCreate && b.AdmissionDate is { } ad) s.AdmissionDate = ad;

        s.Address = b.Address; s.City = b.City; s.State = b.State; s.Pincode = b.Pincode;
        s.Phone = b.Phone; s.Email = b.Email;
        s.FatherName = b.FatherName; s.FatherNameHi = b.FatherNameHi;
        s.FatherPhone = b.FatherPhone; s.FatherOccup = b.FatherOccup;
        s.MotherName = b.MotherName; s.MotherNameHi = b.MotherNameHi;
        s.MotherPhone = b.MotherPhone; s.MotherOccup = b.MotherOccup;
        s.GuardianName = b.GuardianName; s.GuardianPhone = b.GuardianPhone; s.GuardianRel = b.GuardianRel;
        s.Category = b.Category; s.Caste = b.Caste; s.Religion = b.Religion;
        s.MotherTongue = b.MotherTongue;

        // Entity default is "Indian" — do not let an omitted field blank it.
        if (b.Nationality is not null) s.Nationality = b.Nationality;

        s.TransportMode = b.TransportMode; s.BusRoute = b.BusRoute; s.PickupPoint = b.PickupPoint;
        s.House = b.House; s.PhotoUrl = b.PhotoUrl; s.Notes = b.Notes;

        if (b.Status is { } st) s.Status = st;

        // AadharNo's setter masks to XXXXXXXX1234 — the DPDP control lives on
        // the entity, so assigning through it here keeps that guarantee.
        s.AadharNo = b.AadharNo;
        s.AadharDoc = b.AadharDoc; s.AadharDocKey = b.AadharDocKey;
        s.BirthCertNo = b.BirthCertNo; s.BirthDoc = b.BirthDoc; s.BirthDocKey = b.BirthDocKey;

        s.PrevSchool = b.PrevSchool; s.PrevClass = b.PrevClass;
        s.TcNo = b.TcNo; s.TcDate = b.TcDate; s.TcDoc = b.TcDoc; s.TcDocKey = b.TcDocKey;

        if (b.Siblings is not null)
        {
            if (!isCreate) _db.StudentSiblings.RemoveRange(s.Siblings);
            s.Siblings = b.Siblings.Select(x => new StudentSibling
            {
                Name = x.Name,
                Class = x.Class,
                Relation = x.Relation,
                SameSchool = x.SameSchool ?? true,
            }).ToList();
        }

        if (b.PassedExams is not null)
        {
            if (!isCreate) _db.StudentPassedExams.RemoveRange(s.PassedExams);
            s.PassedExams = b.PassedExams.Select(x => new StudentPassedExam
            {
                ExamName = x.ExamName, Institution = x.Institution, Year = x.Year,
                RollNo = x.RollNo, Board = x.Board,
                MaxMarks = x.MaxMarks, ObtainedMarks = x.ObtainedMarks,
            }).ToList();
        }
    }
}
