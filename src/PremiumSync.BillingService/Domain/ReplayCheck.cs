using PremiumSync.BillingService.Data;
using PremiumSync.Contracts;

namespace PremiumSync.BillingService.Domain;

public enum ReplayResult
{
    New,        // never seen this TransactionId: book it
    Duplicate,  // seen it, same data: a retry, return the original and book nothing
    Conflict    // seen it, different data: same key reused for a different transaction, reject
}

/// <summary>
/// Idempotency: the same TransactionId must always produce the same result, no matter how many times it's sent.
/// </summary>
public static class ReplayCheck
{
    public static ReplayResult Evaluate(BillingTransaction? existing, PolicyTransactionMessage incoming)
    {
        if (existing is null) return ReplayResult.New;

        var sameData =
            existing.PolicyNumber == incoming.PolicyNumber &&
            existing.Sequence == incoming.Sequence &&
            existing.Type == incoming.Type &&
            existing.EffectiveDate == incoming.EffectiveDate &&
            existing.Premium == incoming.PremiumChange &&
            existing.FeeAmount == incoming.FeeAmount &&
            existing.CommissionRate == incoming.CommissionRate;

        return sameData ? ReplayResult.Duplicate : ReplayResult.Conflict;
    }
}
