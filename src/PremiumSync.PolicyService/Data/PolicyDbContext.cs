using Microsoft.EntityFrameworkCore;

namespace PremiumSync.PolicyService.Data;

public class PolicyDbContext(DbContextOptions<PolicyDbContext> options) : DbContext(options)
{
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<PolicyTransaction> Transactions => Set<PolicyTransaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Policy>().HasIndex(p => p.PolicyNumber).IsUnique();
        b.Entity<Policy>().Property(p => p.Status).HasConversion<string>();
        b.Entity<Policy>().Ignore(p => p.WrittenPremium);

        b.Entity<PolicyTransaction>().HasIndex(t => t.TransactionId).IsUnique();
        b.Entity<PolicyTransaction>().HasIndex(t => new { t.PolicyId, t.Sequence }).IsUnique();
        b.Entity<PolicyTransaction>().Property(t => t.Type).HasConversion<string>();
        b.Entity<PolicyTransaction>().Property(t => t.SyncStatus).HasConversion<string>();
    }
}
