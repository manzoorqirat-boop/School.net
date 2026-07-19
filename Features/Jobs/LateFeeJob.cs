using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Crypto;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Jobs;

/// <summary>
/// Sends one overdue-fee reminder. Deliberately abstracted out: services/notification.js
/// (email/SMS via SMTP + Twilio) hasn't been ported yet, so this job depends on
/// the interface, not a concrete sender. Swap in a real implementation — and
/// register it in Program.cs — once that port happens; NoOpFeeReminderNotifier
/// just logs so the sweep is observable (and testable) in the meantime.
/// </summary>
public interface IFeeReminderNotifier
{
    Task NotifyOverdueAsync(School school, Student student, FeeInvoice invoice, CancellationToken ct);
}

public sealed class NoOpFeeReminderNotifier(ILogger<NoOpFeeReminderNotifier> log) : IFeeReminderNotifier
{
    public Task NotifyOverdueAsync(School school, Student student, FeeInvoice invoice, CancellationToken ct)
    {
        log.LogInformation(
            "[late-fee] would notify {Student} ({AdmNo}) — invoice {InvoiceNo} overdue since {DueDate}, school {School}",
            student.DisplayName, student.AdmissionNo, invoice.InvoiceNo, invoice.DueDate, school.Name);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Port of the reminder half of services/cronScheduler.js + services/feeScheduler.js
/// (runDailyTick → remindForSchool), wired to Hangfire's daily 01:00 UTC
/// recurring job instead of node-cron.
///
/// Scope note: the Node original's runDailyTick ALSO does invoice generation
/// on feeSchedule.billingDay (feeScheduler.generateForSchool). That half is NOT
/// ported here — it creates invoices from FeeStructure/installments, which is
/// Fees-controller territory (needs an invoice-numbering counter that doesn't
/// exist in .NET yet either) rather than a "late fee" job. Port it alongside
/// the Fees controller's own generate endpoint so the logic has one home, not two.
///
/// Also NOT ported: Node's per-invoice "mark overdue" write + the Redis
/// month-guard around it. FeeInvoice.EffectiveStatus (FeeCluster.cs) already
/// derives Overdue on every read — there is nothing left to mark, and nothing
/// to double-run, so the Redis idempotency lock this job's Node ancestor
/// needed for that write is not needed here either.
///
/// Runs its own unfiltered AppDbContext instance (same pattern as
/// SeederExtensions.SeedDatabaseAsync) because Hangfire executes this outside
/// any HTTP request — the DI-registered TenantContext would have no JWT to
/// read and IsFilterActive would hide every row.
/// </summary>
public sealed class LateFeeJob(
    DbContextOptions<AppDbContext> dbOptions,
    ICryptoService crypto,
    IFeeReminderNotifier notifier,
    ILogger<LateFeeJob> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = new AppDbContext(dbOptions, FixedTenantContext.Unfiltered(), crypto);

        var today = DateTime.UtcNow.Day;

        var schools = await db.Schools
            .Where(s => s.IsActive && s.FeeReminderEnabled && s.FeeReminderDay == today)
            .ToListAsync(ct);

        if (schools.Count == 0)
        {
            log.LogInformation("[late-fee] no schools scheduled for a reminder run today");
            return;
        }

        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        int notified = 0, failed = 0;

        foreach (var school in schools)
        {
            // Pending + past due date == EffectiveStatus.Overdue (FeeCluster.cs).
            // Filtered here rather than via EffectiveStatus itself because that
            // property isn't translatable to SQL — it's evaluated in memory,
            // and Status/DueDate already narrow to exactly the same rows.
            var overdue = await db.FeeInvoices
                .Where(i => i.SchoolId == school.Id &&
                            i.Status == InvoiceStatus.Pending &&
                            i.DueDate <= now)
                .Include(i => i.Student)
                .Take(1000) // matches the Node sweep's .limit(1000) — a safety cap, not a real ceiling
                .ToListAsync(ct);

            foreach (var invoice in overdue)
            {
                try
                {
                    await notifier.NotifyOverdueAsync(school, invoice.Student, invoice, ct);
                    notified++;
                }
                catch (Exception ex)
                {
                    failed++;
                    log.LogError(ex,
                        "[late-fee] reminder failed for invoice {InvoiceId} ({InvoiceNo}), school {School}",
                        invoice.Id, invoice.InvoiceNo, school.Name);
                }
            }
        }

        log.LogInformation(
            "[late-fee] sweep complete: {Schools} school(s) due today, {Notified} reminder(s) sent, {Failed} failed",
            schools.Count, notified, failed);
    }
}
