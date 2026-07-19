using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;

namespace QMSoft.Api.Features;

/// <summary>
/// EnableRetryOnFailure (NpgsqlRetryingExecutionStrategy) forbids a bare
/// BeginTransactionAsync — on a transient-fault retry it can't replay a
/// hand-managed transaction. EF's contract is: run the WHOLE transactional
/// unit inside the execution strategy, so the strategy owns the retry boundary.
///
/// This helper is that wrapper. The delegate does its work and the helper
/// handles begin/commit; throwing rolls back. Every controller that previously
/// wrote `await using var tx = ... BeginTransactionAsync` uses this instead.
/// </summary>
public static class Tx
{
    public static async Task RunAsync(AppDbContext db, Func<Task> work, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await work();
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    /// <summary>Overload for work that needs to signal a caught-and-handled
    /// outcome (e.g. duplicate-key races) without rethrowing — returns whatever
    /// the delegate returns.</summary>
    public static async Task<T> RunAsync<T>(AppDbContext db, Func<Task<T>> work, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await work();
                await tx.CommitAsync(ct);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }
}
