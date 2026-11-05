using Microsoft.EntityFrameworkCore;
using PremiumSync.BillingService.Data;
using PremiumSync.Contracts;
using PremiumSync.PolicyService.Data;
using PremiumSync.Reconciliation.Data;
using PremiumSync.Reconciliation.Engine;

namespace PremiumSync.Tests;

/// <summary>
/// Integration tests for the reconciliation SQL: build real policy and billing SQLite databases
/// (using each service's own migrations), plant one discrepancy, and check the engine reports exactly that.
/// </summary>
public class ReconEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "recon-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _policyDb, _billingDb;
    private readonly PolicyDbContext _pol;
    private readonly BillingDbContext _bil;

    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = Now.AddMinutes(-30);
    private static readonly DateTime LongAgo = Now.AddHours(-2);

    public ReconEngineTests()
    {
        Directory.CreateDirectory(_dir);
        _policyDb = Path.Combine(_dir, "policy.db");
        _billingDb = Path.Combine(_dir, "billing.db");
        _pol = new PolicyDbContext(new DbContextOptionsBuilder<PolicyDbContext>().UseSqlite($"Data Source={_policyDb};Pooling=False").Options);
        _bil = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite($"Data Source={_billingDb};Pooling=False").Options);
        _pol.Database.Migrate();
        _bil.Database.Migrate();
    }

    public void Dispose()
    {
        _pol.Dispose();
        _bil.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    // ---------- Builders ----------

    private Policy AddPolicy(string number, decimal rate = 0.15m, PolicyStatus status = PolicyStatus.InForce)
    {
        var p = new Policy { PolicyNumber = number, InsuredName = "Test", EffectiveDate = new(2026, 1, 1), ExpirationDate = new(2027, 1, 1), CommissionRate = rate, Status = status };
        _pol.Policies.Add(p);
        return p;
    }

    private PolicyTransaction AddTxn(Policy p, int seq, TransactionType type, decimal premium,
        SyncStatus status = SyncStatus.Sent, DateTime? created = null, decimal fee = 0m)
    {
        var t = new PolicyTransaction { Policy = p, Sequence = seq, Type = type, EffectiveDate = new(2026, 1, 1),
            PremiumChange = premium, FeeAmount = fee, SyncStatus = status, CreatedAtUtc = created ?? LongAgo };
        p.Transactions.Add(t);
        return t;
    }

    private void Book(Policy p, PolicyTransaction t, decimal? premium = null, decimal? commission = null, decimal? fee = null) =>
        _bil.Transactions.Add(new BillingTransaction
        {
            SourceTransactionId = t.TransactionId, PolicyNumber = p.PolicyNumber, Sequence = t.Sequence, Type = t.Type,
            EffectiveDate = t.EffectiveDate, Premium = premium ?? t.PremiumChange, FeeAmount = fee ?? t.FeeAmount,
            CommissionRate = p.CommissionRate,
            CommissionAmount = commission ?? Math.Round((premium ?? t.PremiumChange) * p.CommissionRate, 2, MidpointRounding.AwayFromZero),
        });

    /// <summary>A healthy policy: new business + endorsement, both delivered and booked correctly.</summary>
    private Policy AddCleanPolicy(string number)
    {
        var p = AddPolicy(number);
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1200m));
        Book(p, AddTxn(p, 2, TransactionType.Endorsement, 300.82m));
        return p;
    }

    private ReconResult Run()
    {
        _pol.SaveChanges();
        _bil.SaveChanges();
        return ReconEngine.Run(_policyDb, _billingDb, Cutoff);
    }

    private static List<ReasonCode> Codes(ReconResult r) => r.Findings.Select(f => f.ReasonCode).ToList();

    // ---------- Tests ----------

    [Fact]
    public void Clean_data_has_no_findings()
    {
        AddCleanPolicy("PS-000001");
        AddCleanPolicy("PS-000002");
        var result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(2, result.PoliciesChecked);
        Assert.Equal(4, result.TransactionsChecked);
    }

    [Fact]
    public void Sent_but_not_in_billing_is_missing_in_billing()
    {
        var p = AddPolicy("PS-000001");
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1200m));
        AddTxn(p, 2, TransactionType.Endorsement, 300m);   // marked Sent, never booked

        var f = Assert.Single(Run().Findings, x => x.ReasonCode == ReasonCode.MissingInBilling);
        Assert.Equal(2, f.Sequence);
        Assert.Equal(300m, f.PolicyValue);
        Assert.Null(f.BillingValue);
    }

    [Fact]
    public void Old_undelivered_transaction_is_stuck_in_queue()
    {
        var p = AddPolicy("PS-000001");
        AddTxn(p, 1, TransactionType.NewBusiness, 1200m, SyncStatus.DeadLettered, created: LongAgo);

        var f = Assert.Single(Run().Findings);
        Assert.Equal(ReasonCode.StuckInQueue, f.ReasonCode);
        Assert.Contains("DeadLettered", f.Detail);
    }

    [Fact]
    public void Recent_undelivered_transaction_is_in_flight_not_a_finding()
    {
        var p = AddPolicy("PS-000001");
        AddTxn(p, 1, TransactionType.NewBusiness, 1200m, SyncStatus.Failed, created: Now.AddMinutes(-5));

        Assert.Empty(Run().Findings);
    }

    [Fact]
    public void Billing_row_with_no_policy_transaction_is_missing_in_policy()
    {
        var p = AddCleanPolicy("PS-000001");
        var ghost = new PolicyTransaction { Sequence = 3, Type = TransactionType.Endorsement, EffectiveDate = new(2026, 3, 1), PremiumChange = 50m };
        Book(p, ghost);   // booked in billing, never saved on the policy side

        var f = Assert.Single(Run().Findings, x => x.ReasonCode == ReasonCode.MissingInPolicy);
        Assert.Equal(ghost.TransactionId, f.TransactionId);
        Assert.Equal(50m, f.BillingValue);
    }

    [Fact]
    public void Different_premium_is_a_premium_mismatch()
    {
        var p = AddPolicy("PS-000001");
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1200m), premium: 1210m, commission: 180m);

        var codes = Codes(Run());
        Assert.Contains(ReasonCode.PremiumMismatch, codes);
        var f = Run().Findings.First(x => x.ReasonCode == ReasonCode.PremiumMismatch);
        Assert.Equal(1200m, f.PolicyValue);
        Assert.Equal(1210m, f.BillingValue);
        Assert.Equal(10m, f.Difference);
    }

    [Fact]
    public void Different_fee_is_a_fee_mismatch()
    {
        var p = AddPolicy("PS-000001");
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1200m, fee: 25m), fee: 0m);

        Assert.Equal([ReasonCode.FeeMismatch], Codes(Run()));
    }

    [Fact]
    public void Drifted_commission_is_a_commission_mismatch()
    {
        // $1,000 + twenty $1.00 at 12.5% should be 127.50; book 127.60 (the RCA 004 drift).
        var p = AddPolicy("PS-000001", rate: 0.125m);
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1000m), commission: 125.00m);
        for (var i = 0; i < 20; i++) Book(p, AddTxn(p, i + 2, TransactionType.Endorsement, 1m), commission: 0.13m);

        var f = Assert.Single(Run().Findings);
        Assert.Equal(ReasonCode.CommissionMismatch, f.ReasonCode);
        Assert.Equal(127.50m, f.PolicyValue);
        Assert.Equal(127.60m, f.BillingValue);
        Assert.Equal(0.10m, f.Difference);
    }

    [Fact]
    public void Half_cent_commission_rounds_the_same_as_billing()
    {
        // 1,001 x 12.5% = 125.125 -> 125.13 (half away from zero). No false mismatch from floating point.
        var p = AddPolicy("PS-000001", rate: 0.125m);
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1001m), commission: 125.13m);

        Assert.Empty(Run().Findings);
    }

    [Fact]
    public void Commission_is_not_checked_while_a_transaction_is_in_flight()
    {
        var p = AddPolicy("PS-000001");
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1200m));
        AddTxn(p, 2, TransactionType.Endorsement, 300m, SyncStatus.Failed, created: Now.AddMinutes(-1));

        Assert.Empty(Run().Findings);
    }

    [Fact]
    public void Cancelled_in_policy_but_not_in_billing_is_a_status_mismatch()
    {
        // The RCA 005 scenario: the cancellation was rejected, but the policy already says Cancelled.
        var p = AddPolicy("PS-000001", status: PolicyStatus.Cancelled);
        Book(p, AddTxn(p, 1, TransactionType.NewBusiness, 1200m));
        AddTxn(p, 2, TransactionType.Cancellation, -600m, SyncStatus.DeadLettered, created: LongAgo);

        var codes = Codes(Run());
        Assert.Contains(ReasonCode.StatusMismatch, codes);
        Assert.Contains(ReasonCode.StuckInQueue, codes);   // the transaction-level view of the same problem
        Assert.Contains("Cancelled", Run().Findings.First(x => x.ReasonCode == ReasonCode.StatusMismatch).Detail);
    }

    [Fact]
    public void Missing_source_database_is_reported_clearly()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => ReconEngine.Run(Path.Combine(_dir, "nope.db"), _billingDb, Cutoff));
        Assert.Contains("Policy database not found", ex.Message);
    }
}
