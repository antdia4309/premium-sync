using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PremiumSync.PolicyService.Data;

namespace PremiumSync.PolicyService.Sync;

/// <summary>
/// Background job: every few seconds, finds Failed transactions whose retry time has arrived and sends them again.
/// </summary>
public class RetryWorker(IServiceScopeFactory scopes, IOptions<RetryOptions> options, TimeProvider clock, ILogger<RetryWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let one bad batch kill the worker.
                log.LogError(ex, "Retry batch failed");
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        // A fresh scope per batch: DbContext is short-lived by design.
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<TransactionSender>();
        var now = clock.GetUtcNow().UtcDateTime;

        // Due retries, plus anything held behind an earlier transaction (cheap to re-check every tick).
        var due = await db.Transactions.Include(t => t.Policy).ThenInclude(p => p!.Transactions)
            .Where(t => (t.SyncStatus == SyncStatus.Failed && t.NextAttemptAtUtc <= now) || t.SyncStatus == SyncStatus.Held)
            .OrderBy(t => t.PolicyId).ThenBy(t => t.Sequence)   // keep each policy's transactions in order
            .Take(options.Value.BatchSize)
            .ToListAsync(ct);

        // Ordered by policy then sequence, so a blocker sent earlier in this loop releases the next one right away.
        foreach (var txn in due)
        {
            var before = txn.SyncStatus;
            await sender.SendAsync(txn, ct);
            if (before == SyncStatus.Held && txn.SyncStatus == SyncStatus.Held) continue;   // still waiting; don't spam the log
            log.LogInformation("Retry {Attempt} for {PolicyNumber} seq {Sequence}: {Status}",
                txn.AttemptCount, txn.Policy!.PolicyNumber, txn.Sequence, txn.SyncStatus);
        }

        if (due.Count > 0) await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
