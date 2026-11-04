using Microsoft.Extensions.Options;
using PremiumSync.Contracts;
using PremiumSync.PolicyService.Billing;
using PremiumSync.PolicyService.Data;

namespace PremiumSync.PolicyService.Sync;

/// <summary>
/// One place that sends a transaction and records the outcome.
/// Used for the first send (PolicyWorkflow), automatic retries (RetryWorker), and manual resends (error queue).
/// </summary>
public class TransactionSender(BillingClient billing, IOptions<RetryOptions> options, TimeProvider clock)
{
    /// <summary>
    /// Sends and updates the transaction's sync fields. The caller saves changes.
    /// Load the policy and all its transactions first: ordering depends on them.
    /// </summary>
    public async Task SendAsync(PolicyTransaction txn, CancellationToken ct = default)
    {
        var policy = txn.Policy ?? throw new InvalidOperationException("Load the policy with the transaction before sending.");

        // Ordering: never let a transaction reach billing ahead of an earlier one on the same policy.
        var blocker = policy.Transactions
            .Where(t => t.Sequence < txn.Sequence && t.SyncStatus != SyncStatus.Sent)
            .OrderBy(t => t.Sequence)
            .FirstOrDefault();
        if (blocker is not null)
        {
            // Not an attempt, so it doesn't count toward MaxAttempts. The retry worker releases it once the blocker is Sent.
            txn.SyncStatus = SyncStatus.Held;
            txn.SyncError = $"Waiting for sequence {blocker.Sequence} ({blocker.SyncStatus}) to reach billing first.";
            txn.NextAttemptAtUtc = null;
            return;
        }
        var message = new PolicyTransactionMessage(
            txn.TransactionId, policy.PolicyNumber, txn.Sequence, txn.Type,
            txn.EffectiveDate, txn.PremiumChange, txn.FeeAmount, policy.CommissionRate);

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await billing.SendAsync(message, ct);
        txn.AttemptCount++;
        txn.LastAttemptAtUtc = now;

        if (result.Ok)
        {
            txn.SyncStatus = SyncStatus.Sent;
            txn.SyncError = null;
            txn.NextAttemptAtUtc = null;
            return;
        }

        txn.SyncError = result.Error;
        var next = result.Retryable ? RetryPolicy.NextAttemptAt(txn.AttemptCount, now, options.Value) : null;
        if (next is null)
        {
            // Permanent rejection, or out of attempts: park it in the error queue for a person.
            txn.SyncStatus = SyncStatus.DeadLettered;
            txn.NextAttemptAtUtc = null;
        }
        else
        {
            txn.SyncStatus = SyncStatus.Failed;
            txn.NextAttemptAtUtc = next;
        }
    }
}
