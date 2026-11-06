using Microsoft.EntityFrameworkCore;
using PremiumSync.BillingService.Data;
using PremiumSync.Contracts;
using PremiumSync.PolicyService.Data;
using PremiumSync.Reconciliation.Data;
using PremiumSync.Reconciliation.Dev;
using PremiumSync.Reconciliation.Engine;

namespace PremiumSync.Tests;

/// <summary>
/// Builds a small, fully consistent book of business, proves it reconciles clean,
/// then injects each discrepancy and checks reconciliation reports exactly what the injector promised.
/// </summary>
public class DiscrepancyInjectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inject-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _policyDb, _billingDb;

    public DiscrepancyInjectorTests()
    {
        Directory.CreateDirectory(_dir);
        _policyDb = Path.Combine(_dir, "policy.db");
        _billingDb = Path.Combine(_dir, "billing.db");
        SeedBook();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    /// <summary>8 in-force endorsed policies and 2 cancelled endorsed policies, all delivered and booked correctly.</summary>
    private void SeedBook()
    {
        using var pol = new PolicyDbContext(new DbContextOptionsBuilder<PolicyDbContext>().UseSqlite($"Data Source={_policyDb};Pooling=False").Options);
        using var bil = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite($"Data Source={_billingDb};Pooling=False").Options);
        pol.Database.Migrate();
        bil.Database.Migrate();

        for (var i = 1; i <= 10; i++)
        {
            var cancelled = i > 8;
            var p = new Policy { PolicyNumber = $"PS-{i:D6}", InsuredName = "Test", EffectiveDate = new(2026, 1, 1),
                ExpirationDate = new(2027, 1, 1), CommissionRate = 0.15m, Status = cancelled ? PolicyStatus.Cancelled : PolicyStatus.InForce };
            pol.Policies.Add(p);

            var txns = new List<PolicyTransaction>
            {
                new() { Policy = p, Sequence = 1, Type = TransactionType.NewBusiness, EffectiveDate = new(2026, 1, 1), PremiumChange = 1200m, AnnualPremiumChange = 1200m, FeeAmount = 25m },
                new() { Policy = p, Sequence = 2, Type = TransactionType.Endorsement, EffectiveDate = new(2026, 7, 2), PremiumChange = 300.82m, AnnualPremiumChange = 600m },
            };
            if (cancelled)
                txns.Add(new() { Policy = p, Sequence = 3, Type = TransactionType.Cancellation, EffectiveDate = new(2026, 10, 1), PremiumChange = -453.70m });

            decimal premiumBooked = 0m, commissionBooked = 0m;
            foreach (var t in txns)
            {
                t.SyncStatus = SyncStatus.Sent;
                p.Transactions.Add(t);
                var commission = Math.Round((premiumBooked + t.PremiumChange) * p.CommissionRate, 2, MidpointRounding.AwayFromZero) - commissionBooked;
                bil.Transactions.Add(new BillingTransaction
                {
                    SourceTransactionId = t.TransactionId, PolicyNumber = p.PolicyNumber, Sequence = t.Sequence, Type = t.Type,
                    EffectiveDate = t.EffectiveDate, Premium = t.PremiumChange, FeeAmount = t.FeeAmount,
                    CommissionRate = p.CommissionRate, CommissionAmount = commission,
                });
                premiumBooked += t.PremiumChange;
                commissionBooked += commission;
            }
        }
        pol.SaveChanges();
        bil.SaveChanges();
    }

    private ReconResult Recon() => ReconEngine.Run(_policyDb, _billingDb, DateTime.UtcNow.AddMinutes(-30));

    [Fact]
    public void Seeded_book_reconciles_clean() => Assert.Empty(Recon().Findings);

    public static TheoryData<ReasonCode> Kinds => new(DiscrepancyInjector.Kinds);

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_injection_produces_exactly_the_expected_findings(ReasonCode kind)
    {
        var injected = DiscrepancyInjector.Inject(_policyDb, _billingDb, kind);

        var found = Recon().Findings;
        Assert.All(found, f => Assert.Equal(injected.PolicyNumber, f.PolicyNumber));   // nothing else got touched
        Assert.Equal(injected.ExpectedFindings.Order(), found.Select(f => f.ReasonCode.ToString()).Order());
    }

    [Fact]
    public void Inject_all_hits_every_reason_code_on_separate_policies()
    {
        var injected = DiscrepancyInjector.InjectAll(_policyDb, _billingDb);

        Assert.Equal(DiscrepancyInjector.Kinds.Length, injected.Select(i => i.PolicyNumber).Distinct().Count());
        var foundCodes = Recon().Findings.Select(f => f.ReasonCode).ToHashSet();
        Assert.All(DiscrepancyInjector.Kinds, k => Assert.Contains(k, foundCodes));
    }

    [Fact]
    public void Earned_premium_injection_leaves_policy_and_billing_agreeing()
    {
        // The point of this kind: no transaction-level or commission check can see it.
        DiscrepancyInjector.Inject(_policyDb, _billingDb, ReasonCode.EarnedPremiumMismatch);
        Assert.Equal([ReasonCode.EarnedPremiumMismatch], Recon().Findings.Select(f => f.ReasonCode));
    }

    [Fact]
    public void Running_out_of_eligible_policies_explains_how_to_fix_it()
    {
        DiscrepancyInjector.Inject(_policyDb, _billingDb, ReasonCode.EarnedPremiumMismatch);   // uses one cancelled policy
        DiscrepancyInjector.Inject(_policyDb, _billingDb, ReasonCode.EarnedPremiumMismatch);   // uses the other
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiscrepancyInjector.Inject(_policyDb, _billingDb, ReasonCode.EarnedPremiumMismatch));
        Assert.Contains("needs a cancelled policy", ex.Message);
    }
}
