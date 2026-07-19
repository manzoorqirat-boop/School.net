using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QMSoft.Api.Authorization;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Features.Documents;

/// <summary>
/// QuestPDF document generation. Layouts are intentionally simple, single-page,
/// black-on-white — a visual match with the Node PDFKit output is a Phase-4
/// polish task; correctness of the DATA is what matters first.
/// QuestPDF.Settings.License = Community is set at boot (Program.cs).
/// </summary>
public sealed class PdfService
{
    public byte[] Payslip(Domain.Entities.Payroll p) => Document.Create(c =>
        c.Page(page =>
        {
            page.Margin(40); page.Size(PageSizes.A4);
            page.Header().Text($"Payslip — {p.PeriodLabel}").Bold().FontSize(18);
            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Item().Text($"Employee: {p.TeacherName}");
                col.Item().Text($"Net Pay: ₹{p.NetSalary:N2}").Bold();
                col.Item().PaddingTop(8).Text("Earnings").Bold();
                Row(col, "Base", p.BaseSalary); Row(col, "DA", p.Da); Row(col, "HRA", p.Hra);
                Row(col, "TA", p.Ta); Row(col, "Other", p.OtherAllowances);
                Row(col, "Gross", p.GrossSalary);
                col.Item().PaddingTop(8).Text("Deductions").Bold();
                Row(col, "PF", p.Pf); Row(col, "ESI", p.Esi); Row(col, "Prof. Tax", p.ProfessionalTax);
                Row(col, "Income Tax", p.IncomeTax); Row(col, "Leave", p.LeaveDeduction);
                Row(col, "Total Deductions", p.TotalDeductions);
            });
        })).GeneratePdf();

    public byte[] Receipt(Payment pay, FeeInvoice inv) => Document.Create(c =>
        c.Page(page =>
        {
            page.Margin(40); page.Size(PageSizes.A5);
            page.Header().Text("Fee Receipt").Bold().FontSize(16);
            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Item().Text($"Receipt No: {pay.ReceiptNo}");
                col.Item().Text($"Invoice: {inv.InvoiceNo}");
                col.Item().Text($"Student: {inv.StudentName} ({inv.StudentClass})");
                col.Item().Text($"Amount Paid: ₹{pay.Amount:N2}").Bold();
                col.Item().Text($"Method: {pay.Method}");
                col.Item().Text($"Date: {pay.PaidAt:dd-MM-yyyy}");
                col.Item().Text($"Balance: ₹{(inv.Total - inv.AmountPaid):N2}");
            });
        })).GeneratePdf();

    private static void Row(ColumnDescriptor col, string label, decimal amount) =>
        col.Item().Row(r => { r.RelativeItem().Text(label); r.ConstantItem(120).AlignRight().Text($"₹{amount:N2}"); });
}

[ApiController]
public sealed class DocumentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PdfService _pdf;
    public DocumentsController(AppDbContext db, PdfService pdf) { _db = db; _pdf = pdf; }

    [HttpGet("api/payroll/{id:guid}/pdf")]
    [RequirePrivilege("payroll:view")]
    public async Task<IActionResult> Payslip(Guid id, CancellationToken ct)
    {
        var p = await _db.Payrolls.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { error = "Not found" });
        return File(_pdf.Payslip(p), "application/pdf", $"payslip-{p.PeriodLabel}.pdf");
    }

    [HttpGet("api/invoices/{id:guid}/receipt/{paymentId:guid}")]
    [RequirePrivilege("fee:view")]
    public async Task<IActionResult> Receipt(Guid id, Guid paymentId, CancellationToken ct)
    {
        var inv = await _db.FeeInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        var pay = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId && p.InvoiceId == id, ct);
        if (inv is null || pay is null) return NotFound(new { error = "Not found" });
        return File(_pdf.Receipt(pay, inv), "application/pdf", $"receipt-{pay.ReceiptNo}.pdf");
    }
}
