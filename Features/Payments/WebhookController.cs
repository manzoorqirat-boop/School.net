using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Features.Payments;

/// <summary>
/// Razorpay webhook — UNAUTHENTICATED (Razorpay's servers call it), verified by
/// HMAC over the RAW body. Idempotent: a repeated event for an already-recorded
/// payment returns 200 without double-crediting (the uq_payments_rzp partial
/// unique is the hard backstop; the pre-check avoids the 23505 round-trip).
///
/// Always returns 200 for handled-but-ignored cases so Razorpay stops retrying;
/// only signature failure and malformed input get 400.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public sealed class WebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RazorpayService _rzp;
    private readonly ILogger<WebhookController> _log;

    public WebhookController(AppDbContext db, RazorpayService rzp, ILogger<WebhookController> log)
    { _db = db; _rzp = rzp; _log = log; }

    [HttpPost("razorpay")]
    public async Task<IActionResult> Razorpay(CancellationToken ct)
    {
        // Read the EXACT bytes — model binding would re-serialize and break HMAC.
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var raw = ms.ToArray();
        Request.Body.Position = 0;

        var signature = Request.Headers["X-Razorpay-Signature"].ToString();
        if (raw.Length == 0 || string.IsNullOrEmpty(signature))
            return BadRequest(new { error = "Missing signature or body" });

        if (!_rzp.VerifyWebhookSignature(raw, signature))
        {
            _log.LogWarning("Invalid Razorpay webhook signature");
            return BadRequest(new { error = "Invalid signature" });
        }

        var doc = System.Text.Json.JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var evt = root.GetProperty("event").GetString();

        if (evt is "payment.authorized" or "payment.captured")
        {
            var entity = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = entity.GetProperty("id").GetString()!;
            var orderId = entity.TryGetProperty("order_id", out var o) ? o.GetString() : null;
            var amountPaise = entity.GetProperty("amount").GetInt64();

            string? invoiceIdStr = null;
            if (entity.TryGetProperty("notes", out var notes) && notes.ValueKind == System.Text.Json.JsonValueKind.Object)
                invoiceIdStr = notes.TryGetProperty("invoice_id", out var iv) ? iv.GetString()
                             : notes.TryGetProperty("invoiceId", out var iv2) ? iv2.GetString() : null;

            if (!Guid.TryParse(invoiceIdStr, out var invoiceId))
                return Ok(new { received = true });   // nothing to do, don't retry

            // Idempotency: already recorded?
            if (await _db.Payments.AsNoTracking().IgnoreQueryFilters()
                    .AnyAsync(p => p.RazorpayPaymentId == paymentId, ct))
                return Ok(new { received = true, duplicate = true });

            var inv = await _db.FeeInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
            if (inv is null) return Ok(new { received = true });

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.Payments.Add(new Payment
                {
                    SchoolId = inv.SchoolId, InvoiceId = inv.Id, StudentId = inv.StudentId,
                    ReceiptNo = $"RZP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
                    Amount = amountPaise / 100m, Method = PaymentMethod.Razorpay,
                    RazorpayOrderId = orderId, RazorpayPaymentId = paymentId,
                    RazorpayVerified = true, Status = PaymentStatus.Success, PaidAt = DateTime.UtcNow,
                });
                inv.AmountPaid += amountPaise / 100m;
                inv.RecomputeStoredStatus();
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)   // race: another webhook won the insert
            { await tx.RollbackAsync(ct); return Ok(new { received = true, duplicate = true }); }
        }

        return Ok(new { received = true });
    }
}
