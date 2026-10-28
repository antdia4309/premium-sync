using Microsoft.EntityFrameworkCore;
using PremiumSync.Contracts;
using PremiumSync.PolicyService.Billing;
using PremiumSync.PolicyService.Data;

namespace PremiumSync.PolicyService.Domain;

public record WorkflowResult(PolicyTransaction? Transaction, Dictionary<string, string[]>? Errors = null, bool NotFound = false)
{
    public static WorkflowResult Invalid(string field, string message) => new(null, new() { [field] = [message] });
    public static WorkflowResult Missing() => new(null, NotFound: true);
}

/// <summary>
/// The policy-side business rules: create, endorse, cancel.
/// Every premium-bearing transaction is saved first, then sent to billing.
/// </summary>
public class PolicyWorkflow(PolicyDbContext db, BillingClient billing)
{
    public async Task<WorkflowResult> CreateAsync(CreatePolicyRequest req, CancellationToken ct = default)
    {
        var count = await db.Policies.CountAsync(ct);
        var policy = new Policy
        {
            PolicyNumber = $"PS-{count + 1:D6}",
            InsuredName = req.InsuredName.Trim(),
            EffectiveDate = req.EffectiveDate,
            ExpirationDate = req.EffectiveDate.AddYears(1),
            CommissionRate = req.CommissionRate,
        };
        db.Policies.Add(policy);

        var txn = new PolicyTransaction
        {
            Policy = policy,
            Sequence = 1,
            Type = TransactionType.NewBusiness,
            EffectiveDate = req.EffectiveDate,
            PremiumChange = Math.Round(req.AnnualPremium, 2),
            FeeAmount = Math.Round(req.FeeAmount, 2),
        };
        return await SaveAndSendAsync(policy, txn, ct);
    }

    public async Task<WorkflowResult> EndorseAsync(string policyNumber, EndorseRequest req, CancellationToken ct = default)
    {
        var policy = await LoadAsync(policyNumber, ct);
        if (policy is null) return WorkflowResult.Missing();
        if (policy.Status == PolicyStatus.Cancelled) return WorkflowResult.Invalid("policy", "Policy is cancelled.");
        if (!InTerm(policy, req.EffectiveDate)) return WorkflowResult.Invalid(nameof(req.EffectiveDate), "Effective date must fall within the policy term.");

        var txn = new PolicyTransaction
        {
            Policy = policy,
            Sequence = NextSequence(policy),
            Type = TransactionType.Endorsement,
            EffectiveDate = req.EffectiveDate,
            PremiumChange = PremiumMath.ProRata(req.AnnualPremiumChange, policy.EffectiveDate, policy.ExpirationDate, req.EffectiveDate),
            FeeAmount = Math.Round(req.FeeAmount, 2),
        };
        return await SaveAndSendAsync(policy, txn, ct);
    }

    public async Task<WorkflowResult> CancelAsync(string policyNumber, CancelRequest req, CancellationToken ct = default)
    {
        var policy = await LoadAsync(policyNumber, ct);
        if (policy is null) return WorkflowResult.Missing();
        if (policy.Status == PolicyStatus.Cancelled) return WorkflowResult.Invalid("policy", "Policy is already cancelled.");
        if (!InTerm(policy, req.EffectiveDate)) return WorkflowResult.Invalid(nameof(req.EffectiveDate), "Cancellation date must fall within the policy term.");

        policy.Status = PolicyStatus.Cancelled;
        var txn = new PolicyTransaction
        {
            Policy = policy,
            Sequence = NextSequence(policy),
            Type = TransactionType.Cancellation,
            EffectiveDate = req.EffectiveDate,
            PremiumChange = PremiumMath.CancellationReturn(policy.WrittenPremium, policy.EffectiveDate, policy.ExpirationDate, req.EffectiveDate),
            FeeAmount = 0m,
        };
        return await SaveAndSendAsync(policy, txn, ct);
    }

    private async Task<WorkflowResult> SaveAndSendAsync(Policy policy, PolicyTransaction txn, CancellationToken ct)
    {
        // Save first so the transaction exists even if billing is down.
        policy.Transactions.Add(txn);
        await db.SaveChangesAsync(ct);

        var message = new PolicyTransactionMessage(
            txn.TransactionId, policy.PolicyNumber, txn.Sequence, txn.Type,
            txn.EffectiveDate, txn.PremiumChange, txn.FeeAmount, policy.CommissionRate);

        var (ok, error) = await billing.SendAsync(message, ct);
        txn.SyncStatus = ok ? SyncStatus.Sent : SyncStatus.Failed;
        txn.SyncError = error;
        await db.SaveChangesAsync(ct);

        return new WorkflowResult(txn);
    }

    private Task<Policy?> LoadAsync(string policyNumber, CancellationToken ct) =>
        db.Policies.Include(p => p.Transactions).FirstOrDefaultAsync(p => p.PolicyNumber == policyNumber, ct);

    private static bool InTerm(Policy p, DateOnly d) => d >= p.EffectiveDate && d <= p.ExpirationDate;

    private static int NextSequence(Policy p) => p.Transactions.Count == 0 ? 1 : p.Transactions.Max(t => t.Sequence) + 1;
}
