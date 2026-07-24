using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Features.Payments;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Fees;

// Online payment + bulk invoice generation. Split from InvoicesController to
// keep the Razorpay dependency isolated.
[ApiController]
[Route("api/invoices")]
public sealed class FeeOnlineController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly RazorpayService _rzp;
    public FeeOnlineController(AppDbContext db, ITenantContext tenant, IAuditWriter audit, RazorpayService rzp)
    { _db = db; _tenant = tenant; _audit = audit; _rzp = rzp; }

    // Create a Razorpay order for the outstanding balance. (Order creation via
    // Razorpay's API needs the SDK/HTTP call in production; here we return the
    // amount + key so the frontend can open checkout. The webhook/verify path
    // records the actual payment.)
    [HttpPost("{id:guid}/razorpay/order")]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> CreateOrder(Guid id, CancellationToken ct)
    {
        var inv = await _db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return NotFound(new { error = "Not found" });
        if (inv.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            return BadRequest(new { error = $"Invoice already {inv.Status.ToString().ToLowerInvariant()}" });

        var balance = inv.Total - inv.AmountPaid;
        if (balance <= 0)
            return BadRequest(new { error = "This invoice has no outstanding balance to pay online.", code = "NO_BALANCE" });

        return Ok(new
        {
            amount = (long)Math.Round(balance * 100),   // paise
            currency = "INR",
            invoice = new { inv.InvoiceNo, inv.Total, balance },
            notes = new { invoice_id = inv.Id.ToString() },
        });
    }

    public sealed record VerifyRequest(string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);

    [HttpPost("{id:guid}/razorpay/verify")]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> Verify(Guid id, [FromBody] VerifyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.RazorpayOrderId) || string.IsNullOrEmpty(req.RazorpayPaymentId) || string.IsNullOrEmpty(req.RazorpaySignature))
            return BadRequest(new { error = "Missing Razorpay fields" });

        var inv = await _db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return NotFound(new { error = "Not found" });

        var keySecret = await _db.Schools.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == inv.SchoolId).Select(s => s.RazorpayKeySecret).FirstOrDefaultAsync(ct);

        if (!_rzp.VerifyPaymentSignature(keySecret ?? "", req.RazorpayOrderId, req.RazorpayPaymentId, req.RazorpaySignature))
        {
            await _audit.WriteAsync("fee.razorpay_verify_failed", "invoice", id.ToString(), ct: ct);
            return BadRequest(new { error = "Signature verification failed" });
        }

        var existing = await _db.Payments.FirstOrDefaultAsync(p => p.RazorpayPaymentId == req.RazorpayPaymentId, ct);
        if (existing is not null) return Ok(new { payment = existing, invoice = inv });

        var balance = inv.Total - inv.AmountPaid;
        var payment = new Payment
        {
            SchoolId = inv.SchoolId, InvoiceId = inv.Id, StudentId = inv.StudentId,
            ReceiptNo = $"RZP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
            Amount = balance, Method = PaymentMethod.Razorpay,
            RazorpayOrderId = req.RazorpayOrderId, RazorpayPaymentId = req.RazorpayPaymentId,
            RazorpayVerified = true, Status = PaymentStatus.Success, PaidAt = DateTime.UtcNow,
        };
        try
        {
            await Tx.RunAsync(_db, async () =>
            {
                _db.Payments.Add(payment);
                inv.AmountPaid += balance;
                inv.RecomputeStoredStatus();
                await _db.SaveChangesAsync(ct);
            }, ct);
        }
        catch (DbUpdateException)   // webhook beat us to it (uq_payments_rzp)
        {
            var p = await _db.Payments.FirstOrDefaultAsync(x => x.RazorpayPaymentId == req.RazorpayPaymentId, ct);
            return Ok(new { payment = p, invoice = inv });
        }
        await _audit.WriteAsync("fee.razorpay_verified", "payment", payment.Id.ToString(), ct: ct);
        return Ok(new { payment, invoice = inv });
    }

    // Bulk-generate invoices for a structure's installment (or full) — no
    // external dep, pure logic.
    public sealed record GenerateRequest(Guid FeeStructureId, string? InstallmentName, List<Guid>? StudentIds);

    [HttpPost("generate")]
    [RequirePrivilege("fee:create")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest req, CancellationToken ct)
    {
        var fs = await _db.FeeStructures.AsNoTracking().Include(f => f.Heads).Include(f => f.Installments)
            .FirstOrDefaultAsync(f => f.Id == req.FeeStructureId, ct);
        if (fs is null) return NotFound(new { error = "Fee structure not found" });

        var studentsQ = _db.Students.AsNoTracking().Where(s => s.Class == fs.Class && s.Status == StudentStatus.Active);
        if (!string.IsNullOrEmpty(fs.Section)) studentsQ = studentsQ.Where(s => s.Section == fs.Section);
        if (req.StudentIds is { Count: > 0 }) studentsQ = studentsQ.Where(s => req.StudentIds.Contains(s.Id));
        var students = await studentsQ.ToListAsync(ct);

        var installment = fs.Installments.FirstOrDefault(i => i.Name == req.InstallmentName);
        var annualTotal = fs.TotalAmount(includeOptional: false);

        // Refuse to create zero-value invoices.
        //
        // Previously a structure whose heads were all optional, all zero, or
        // simply absent produced N invoices of Total = 0. The dashboard then
        // showed "Outstanding: 0" and everything looked like it had worked —
        // the worst outcome, because the error only surfaces when a parent is
        // never billed. Say exactly which of the three it was.
        if (annualTotal <= 0)
        {
            var billable = fs.Heads.Count(h => !h.IsOptional);
            var reason =
                fs.Heads.Count == 0 ? "it has no fee heads"
                : billable == 0     ? "every fee head is marked optional"
                :                     "every non-optional fee head has an amount of 0";
            return BadRequest(new
            {
                error = $"Fee structure \"{fs.Name}\" totals \u20b90 because {reason}. "
                      + "Fix the structure before generating invoices.",
                code = "FEE_STRUCTURE_EMPTY",
                heads = fs.Heads.Count,
                billableHeads = billable,
                annualTotal,
            });
        }

        var amount = installment is not null ? Math.Round(annualTotal * installment.Percentage / 100m, 2) : annualTotal;
        var dueDate = installment?.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        int created = 0, skipped = 0;
        foreach (var s in students)
        {
            var invoiceNo = $"INV-{fs.AcademicYear}-{s.AdmissionNo}-{req.InstallmentName ?? "FULL"}";
            if (await _db.FeeInvoices.AnyAsync(i => i.InvoiceNo == invoiceNo, ct)) { skipped++; continue; }

            var inv = new FeeInvoice
            {
                SchoolId = _tenant.SchoolId ?? Guid.Empty, StudentId = s.Id, FeeStructureId = fs.Id,
                InvoiceNo = invoiceNo, AcademicYear = fs.AcademicYear,
                StudentName = $"{s.FirstName} {s.LastName}".Trim(), StudentAdmNo = s.AdmissionNo,
                StudentClass = s.Class, StudentSection = s.Section,
                InstallmentName = req.InstallmentName, DueDate = dueDate,
                Subtotal = amount, Total = amount,
                Lines = fs.Heads.Where(h => !h.IsOptional)
                    .Select(h => new FeeInvoiceLine { HeadName = h.Name, Amount = installment is not null ? Math.Round(h.Amount * installment.Percentage / 100m, 2) : h.Amount })
                    .ToList(),
            };
            inv.RecomputeStoredStatus();
            _db.FeeInvoices.Add(inv);
            created++;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("invoice.generate", "invoice",
            metaJson: System.Text.Json.JsonSerializer.Serialize(new { req.FeeStructureId, created, skipped }), ct: ct);
        return Ok(new { created, skipped });
    }
}
