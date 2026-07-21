using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Common;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Fees;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    public InvoicesController(AppDbContext db, ITenantContext tenant, IAuditWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> List([FromQuery] Guid? studentId, [FromQuery] string? status,
        [FromQuery] string? academicYear, [FromQuery] int? page, [FromQuery] int? limit, CancellationToken ct)
    {
        var q = _db.FeeInvoices.AsNoTracking().Include(i => i.Lines).AsQueryable();

        // Row-scope: parent/student see only their invoices.
        if (_tenant.Role == "parent")
        {
            var owned = await ParentOfIdsAsync(ct);
            q = owned.Count == 0 ? q.Where(_ => false) : q.Where(i => owned.Contains(i.StudentId));
        }
        else if (_tenant.Role == "student" && _tenant.StudentId is { } sid)
            q = q.Where(i => i.StudentId == sid);

        if (studentId is { } s) q = q.Where(i => i.StudentId == s);
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(i => i.AcademicYear == academicYear);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<InvoiceStatus>(status, true, out var st))
            q = q.Where(i => i.Status == st);   // note: filters STORED status; overdue is derived

        var p = Math.Max(1, page ?? 1); var l = Math.Clamp(limit ?? PageInfo.DefaultLimit, 1, 100);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(i => i.DueDate).Skip((p - 1) * l).Take(l).ToListAsync(ct);
        return Ok(new Paged<FeeInvoice> { Items = items, Pagination = PageInfo.Create(total, p, l) });
    }

    [HttpGet("{id:guid}")]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var inv = await _db.FeeInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return NotFound(new { error = "Not found" });
        if (!await CanAccessInvoiceAsync(inv, ct)) return StatusCode(403, new { error = "Forbidden" });
        return Ok(inv);
    }

    public sealed record DiscountRequest(decimal Discount, string? Reason);
    [HttpPost("{id:guid}/discount")]
    [RequirePrivilege("fee:create")]
    public async Task<IActionResult> Discount(Guid id, [FromBody] DiscountRequest req, CancellationToken ct)
    {
        var inv = await _db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return NotFound(new { error = "Not found" });
        if (req.Discount < 0 || req.Discount > inv.Subtotal)
            return BadRequest(new { error = "Discount out of range" });
        inv.Discount = req.Discount; inv.DiscountReason = req.Reason;
        inv.Total = inv.Subtotal - inv.Discount + inv.LateFee;
        inv.RecomputeStoredStatus();
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("invoice.discount", "invoice", id.ToString(), ct: ct);
        return Ok(inv);
    }

    // ── POST /:id/pay-offline — idempotent, updates amountPaid + status ────
    public sealed record PayOfflineRequest(decimal Amount, string? Method, string? ChequeNo, string? ChequeBank,
        DateOnly? ChequeDate, string? TransactionRef, string? Notes, string? IdempotencyKey);

    [HttpPost("{id:guid}/pay-offline")]
    [RequirePrivilege("fee:collect")]
    public async Task<IActionResult> PayOffline(Guid id, [FromBody] PayOfflineRequest req, CancellationToken ct)
    {
        if (req.Amount <= 0) return BadRequest(new { error = "Amount must be a positive number", code = "AMOUNT_INVALID" });
        if (req.Amount > 99_999_999) return BadRequest(new { error = "Amount unreasonably large — please re-check", code = "AMOUNT_TOO_LARGE" });

        var valid = new[] { "cash", "cheque", "upi", "card", "bank_transfer" };
        if (string.IsNullOrEmpty(req.Method) || !valid.Contains(req.Method))
            return BadRequest(new { error = $"Invalid payment method. Allowed: {string.Join(", ", valid)}", code = "METHOD_INVALID" });
        if (req.Method == "cheque")
        {
            if (string.IsNullOrWhiteSpace(req.ChequeNo)) return BadRequest(new { error = "Cheque number is required for cheque payments", code = "CHEQUE_NO_REQUIRED" });
            if (string.IsNullOrWhiteSpace(req.ChequeBank)) return BadRequest(new { error = "Bank name is required for cheque payments", code = "CHEQUE_BANK_REQUIRED" });
        }
        if (req.Method is "bank_transfer" or "upi" or "card" && string.IsNullOrEmpty(req.TransactionRef))
            return BadRequest(new { error = "Transaction reference is required for this payment method", code = "TXN_REF_REQUIRED" });

        var inv = await _db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return NotFound(new { error = "Not found" });
        if (inv.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            return BadRequest(new { error = $"Invoice already {inv.Status.ToString().ToLowerInvariant()}" });

        var outstanding = inv.Total - inv.AmountPaid;
        if (req.Amount > outstanding)
            return BadRequest(new { error = $"Amount exceeds outstanding balance (₹{outstanding:F2} remaining)" });

        // Idempotency: a client-supplied key makes network retries safe.
        if (!string.IsNullOrEmpty(req.IdempotencyKey))
        {
            var dup = await _db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.InvoiceId == id && p.IdempotencyKey == req.IdempotencyKey, ct);
            if (dup is not null) return Ok(new { payment = dup, invoice = inv, idempotent = true });
        }

        Enum.TryParse<PaymentMethod>(req.Method, true, out var method);

        // Cheques are NOT money in the bank yet — record them as Pending and DON'T
        // credit the invoice until the cheque clears (PATCH .../cheque-status).
        // All other methods clear immediately. (Matches Node feeController.)
        var isCheque = method == PaymentMethod.Cheque;

        var payment = new Payment
        {
            SchoolId = _tenant.SchoolId ?? Guid.Empty, InvoiceId = id, StudentId = inv.StudentId,
            ReceiptNo = $"RCPT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
            Amount = req.Amount, Method = method,
            Status = isCheque ? PaymentStatus.Pending : PaymentStatus.Success,
            ChequeNo = req.ChequeNo, ChequeBank = req.ChequeBank, ChequeDate = req.ChequeDate,
            TransactionRef = req.TransactionRef, Notes = req.Notes, IdempotencyKey = req.IdempotencyKey,
            CollectedByUserId = _tenant.UserId, PaidAt = DateTime.UtcNow,
        };

        await Tx.RunAsync(_db, async () =>
        {
            _db.Payments.Add(payment);
            if (!isCheque)
            {
                inv.AmountPaid += req.Amount;           // denormalised sum
                inv.RecomputeStoredStatus();            // pending→partial→paid
            }
            await _db.SaveChangesAsync(ct);
        }, ct);    // 23505 on idem/receipt → 409 envelope

        await _audit.WriteAsync("invoice.pay_offline", "payment", payment.Id.ToString(),
            metaJson: System.Text.Json.JsonSerializer.Serialize(new { invoiceId = id, amount = req.Amount, method = req.Method }), ct: ct);
        return Ok(new { payment, invoice = inv });
    }

    // ── PATCH /:id/payments/:paymentId/cheque-status ──────────────────────
    // Clear or bounce a pending cheque. Only a pending cheque can transition;
    // on 'success' the invoice is finally credited. (Ported from Node.)
    public sealed record ChequeStatusRequest(string? Status, DateOnly? ChequeDate);

    [HttpPatch("{id:guid}/payments/{paymentId:guid}/cheque-status")]
    [RequirePrivilege("fee:collect")]
    public async Task<IActionResult> UpdateChequeStatus(
        Guid id, Guid paymentId, [FromBody] ChequeStatusRequest req, CancellationToken ct)
    {
        if (req.Status is not ("success" or "bounced"))
            return BadRequest(new { error = "status must be 'success' or 'bounced'", code = "INVALID_STATUS" });

        var inv = await _db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return NotFound(new { error = "Invoice not found" });

        var payment = await _db.Payments.FirstOrDefaultAsync(
            p => p.Id == paymentId && p.InvoiceId == id && p.Method == PaymentMethod.Cheque, ct);
        if (payment is null) return NotFound(new { error = "Cheque payment not found" });

        if (payment.Status != PaymentStatus.Pending)
            return BadRequest(new
            {
                error = $"Cheque is already '{payment.Status.ToString().ToLowerInvariant()}' — only pending cheques can be updated",
                code = "CHEQUE_NOT_PENDING",
            });

        await Tx.RunAsync(_db, async () =>
        {
            payment.Status = req.Status == "success" ? PaymentStatus.Success : PaymentStatus.Failed;
            if (req.ChequeDate is { } cd) payment.ChequeDate = cd;

            // Only credit the invoice when the cheque actually clears.
            if (req.Status == "success")
            {
                inv.AmountPaid += payment.Amount;
                inv.RecomputeStoredStatus();
            }
            await _db.SaveChangesAsync(ct);
        }, ct);

        await _audit.WriteAsync($"fee.cheque_{req.Status}", "payment", payment.Id.ToString(),
            metaJson: System.Text.Json.JsonSerializer.Serialize(
                new { invoiceNo = inv.InvoiceNo, amount = payment.Amount, chequeNo = payment.ChequeNo }), ct: ct);

        return Ok(new { payment, invoice = inv });
    }

    // ── Reports (aggregations) ────────────────────────────────────────────
    [HttpGet("reports/summary")]
    [RequirePrivilege("fee:report")]
    public async Task<IActionResult> Summary([FromQuery] string? academicYear, CancellationToken ct)
    {
        var q = _db.FeeInvoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(academicYear)) q = q.Where(i => i.AcademicYear == academicYear);
        var totalBilled = await q.SumAsync(i => (decimal?)i.Total, ct) ?? 0m;
        var totalCollected = await q.SumAsync(i => (decimal?)i.AmountPaid, ct) ?? 0m;
        return Ok(new { totalBilled, totalCollected, outstanding = totalBilled - totalCollected });
    }

    [HttpGet("reports/collection")]
    [RequirePrivilege("fee:report")]
    public async Task<IActionResult> Collection([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var start = (from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30))).ToDateTime(TimeOnly.MinValue);
        var end = (to ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MaxValue);
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt <= end && p.Status == PaymentStatus.Success)
            .GroupBy(p => p.Method)
            .Select(g => new { method = g.Key.ToString(), count = g.Count(), total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        return Ok(new { from = start, to = end, byMethod = rows, total = rows.Sum(r => r.total) });
    }

    [HttpGet("reports/outstanding")]
    [RequirePrivilege("fee:report")]
    public async Task<IActionResult> Outstanding([FromQuery] string? @class, CancellationToken ct)
    {
        var q = _db.FeeInvoices.AsNoTracking().Where(i => i.AmountPaid < i.Total && i.Status != InvoiceStatus.Cancelled);
        if (!string.IsNullOrEmpty(@class)) q = q.Where(i => i.StudentClass == @class);
        var items = await q.OrderByDescending(i => i.Total - i.AmountPaid)
            .Select(i => new { i.Id, i.InvoiceNo, i.StudentName, i.StudentClass, i.Total, i.AmountPaid, balance = i.Total - i.AmountPaid, i.DueDate })
            .ToListAsync(ct);
        return Ok(new { items, count = items.Count, totalOutstanding = items.Sum(i => i.balance) });
    }

    // ── helpers ───────────────────────────────────────────────────────────
    private async Task<bool> CanAccessInvoiceAsync(FeeInvoice inv, CancellationToken ct)
    {
        if (_tenant.Role == "parent") return (await ParentOfIdsAsync(ct)).Contains(inv.StudentId);
        if (_tenant.Role == "student") return _tenant.StudentId == inv.StudentId;
        return true;
    }

    private async Task<IReadOnlyCollection<Guid>> ParentOfIdsAsync(CancellationToken ct)
    {
        if (_tenant.Role != "parent" || _tenant.UserId is not { } uid) return Array.Empty<Guid>();
        return await _db.Users.IgnoreQueryFilters().Where(u => u.Id == uid)
            .SelectMany(u => u.ParentOf.Select(s => s.Id)).ToListAsync(ct);
    }
}
