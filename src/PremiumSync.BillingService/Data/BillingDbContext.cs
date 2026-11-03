using Microsoft.EntityFrameworkCore;
using PremiumSync.Contracts;

namespace PremiumSync.BillingService.Data;

/// <summary>One row per policy transaction billing has accepted.</summary>
public class BillingTransaction
{
    public int Id { get; set; }
    public Guid SourceTransactionId { get; set; }   // the policy system's TransactionId
    public string PolicyNumber { get; set; } = "";
    public int Sequence { get; set; }
    public TransactionType Type { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal Premium { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<BillingTransaction> Transactions => Set<BillingTransaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BillingTransaction>().HasIndex(t => t.PolicyNumber);
        // Unique: the database itself refuses a second row for the same source transaction.
        // This is the safety net behind the idempotency check in the inbound endpoint.
        b.Entity<BillingTransaction>().HasIndex(t => t.SourceTransactionId).IsUnique();
        b.Entity<BillingTransaction>().Property(t => t.Type).HasConversion<string>();
    }
}
