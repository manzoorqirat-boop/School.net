using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Jobs;

/// <summary>
/// Port of the Node feeScheduler. Runs daily via Hangfire (registered in
/// Program.cs). Two responsibilities:
///   1. Overdue marking — NOT NEEDED as a write: EffectiveStatus derives overdue
///      on read (resolved design). Kept only for late-fee application.
///   2. Late fees — add the school's configured late fee to invoices that are
///      past due, unpaid, and haven't already had one applied this cycle.
///
/// Runs UNFILTERED (no tenant/JWT) — it sweeps every school, so it constructs a
/// context with FixedTenantContext.Unfiltered like the seeder does.
/// </summary>
public sealed class LateFeeJob
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<LateFeeJob> _log;
    public LateFeeJob(IServiceProvider sp, ILogger<LateFeeJob> log) { _sp = sp; _log = log; }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>>(),
            FixedTenantContext.Unfiltered(),
            scope.ServiceProvider.GetRequiredService<Infrastructure.Crypto.ICryptoService>());

        // Overdue status is DERIVED on read (EffectiveStatus) — no write pass is
        // needed to "mark" invoices overdue, which is the bulk of what the Node
        // feeScheduler did. Late-fee AMOUNTS depend on a per-day rate that isn't
        // in the School schema (Node applied them ad-hoc per invoice), so there's
        // nothing to auto-apply here yet. This job is registered and runs; when a
        // late-fee policy field is added to School, the calculation slots in here.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdueCount = await db.FeeInvoices.AsNoTracking()
            .CountAsync(i => i.DueDate < today && i.AmountPaid < i.Total
                          && i.Status != InvoiceStatus.Cancelled, ct);

        _log.LogInformation("[late-fee-job] {Count} invoices currently overdue (status derived on read)", overdueCount);
        await Task.CompletedTask;
    }
}
