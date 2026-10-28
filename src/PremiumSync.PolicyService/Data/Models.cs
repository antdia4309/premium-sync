using PremiumSync.Contracts;

namespace PremiumSync.PolicyService.Data;

public enum PolicyStatus { InForce, Cancelled }

public enum SyncStatus { Pending, Sent, Failed }

public class Policy
{
    public int Id { get; set; }
    public string PolicyNumber { get; set; } = "";
    public string InsuredName { get; set; } = "";
    public DateOnly EffectiveDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public decimal CommissionRate { get; set; }
    public PolicyStatus Status { get; set; } = PolicyStatus.InForce;
    public List<PolicyTransaction> Transactions { get; set; } = [];

    public decimal WrittenPremium => Transactions.Sum(t => t.PremiumChange);
}

public class PolicyTransaction
{
    public int Id { get; set; }
    public Guid TransactionId { get; set; } = Guid.NewGuid();
    public int PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public int Sequence { get; set; }
    public TransactionType Type { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal PremiumChange { get; set; }
    public decimal FeeAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Did billing accept it? Phase 2 adds retries for Failed rows.
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    public string? SyncError { get; set; }
}
