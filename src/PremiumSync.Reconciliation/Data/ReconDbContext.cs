using Microsoft.EntityFrameworkCore;

namespace PremiumSync.Reconciliation.Data;

public enum ReasonCode
{
    MissingInBilling,    // policy says Sent, billing has nothing
    StuckInQueue,        // not delivered, older than the cutoff
    MissingInPolicy,     // billing has something the policy system doesn't
    PremiumMismatch,     // same transaction, different premium
    FeeMismatch,         // same transaction, different fee
    CommissionMismatch,  // policy-level: booked commission vs. expected
    StatusMismatch,      // policy-level: systems disagree on cancelled vs. in force
    EarnedPremiumMismatch // policy-level: written premium doesn't match an independent recalculation
}

/// <summary>One reconciliation run: when it ran, how much it checked, what it found.</summary>
public class ReconRun
{
    public int Id { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public DateTime CutoffUtc { get; set; }
    public int PoliciesChecked { get; set; }
    public int TransactionsChecked { get; set; }
    public int FindingCount { get; set; }
    public List<ReconFinding> Findings { get; set; } = [];
}

/// <summary>One discrepancy. Values are null when they don't apply (e.g. no billing value for MissingInBilling).</summary>
public class ReconFinding
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public ReasonCode ReasonCode { get; set; }
    public string PolicyNumber { get; set; } = "";
    public Guid? TransactionId { get; set; }
    public int? Sequence { get; set; }
    public decimal? PolicyValue { get; set; }
    public decimal? BillingValue { get; set; }
    public decimal? Difference { get; set; }
    public string Detail { get; set; } = "";
}

public class ReconDbContext(DbContextOptions<ReconDbContext> options) : DbContext(options)
{
    public DbSet<ReconRun> Runs => Set<ReconRun>();
    public DbSet<ReconFinding> Findings => Set<ReconFinding>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ReconRun>().HasMany(r => r.Findings).WithOne().HasForeignKey(f => f.RunId);
        b.Entity<ReconFinding>().Property(f => f.ReasonCode).HasConversion<string>();
        b.Entity<ReconFinding>().HasIndex(f => new { f.RunId, f.ReasonCode });
        b.Entity<ReconFinding>().HasIndex(f => f.PolicyNumber);
    }
}
