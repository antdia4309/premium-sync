using PremiumSync.BillingService.Domain;
using PremiumSync.Contracts;
using PremiumSync.PolicyService.Data;
using PremiumSync.PolicyService.Domain;

namespace PremiumSync.Tests;

public class PolicyRulesTests
{
    private static readonly Policy P = new() { EffectiveDate = new(2026, 1, 1), ExpirationDate = new(2027, 1, 1) };

    // ---------- Change dates ----------

    [Fact]
    public void Change_on_the_effective_date_is_allowed() =>
        Assert.Null(PolicyRules.CheckChangeDate(P, new(2026, 1, 1), "A cancellation"));

    [Fact]
    public void Change_the_day_before_expiration_is_allowed() =>
        Assert.Null(PolicyRules.CheckChangeDate(P, new(2026, 12, 31), "A cancellation"));

    [Fact]
    public void Change_on_the_expiration_date_is_rejected() =>
        Assert.Contains("before the policy expires", PolicyRules.CheckChangeDate(P, new(2027, 1, 1), "A cancellation"));

    [Fact]
    public void Change_before_the_effective_date_is_rejected() =>
        Assert.Contains("before the policy's effective date", PolicyRules.CheckChangeDate(P, new(2025, 12, 31), "An endorsement"));

    // ---------- Written premium floor ----------

    [Fact]
    public void Reduction_below_zero_is_rejected()
    {
        var error = PolicyRules.CheckWrittenPremium(1200m, -2000m);
        Assert.Contains("-$800.00", error);
    }

    [Fact]
    public void Reduction_to_exactly_zero_is_allowed() =>
        Assert.Null(PolicyRules.CheckWrittenPremium(1200m, -1200m));

    [Fact]
    public void Zero_change_is_allowed() =>
        Assert.Null(PolicyRules.CheckWrittenPremium(1200m, 0m));

    // ---------- Billing account floor ----------

    private static PolicyTransactionMessage Msg(decimal premium) =>
        new(Guid.NewGuid(), "PS-000001", 2, TransactionType.Endorsement, new(2026, 3, 1), premium, 0m, 0.15m);

    [Fact]
    public void Billing_rejects_a_negative_account_balance() =>
        Assert.NotEmpty(TransactionValidator.ValidateAgainstAccount(1200m, Msg(-2000m)));

    [Fact]
    public void Billing_allows_an_account_at_exactly_zero() =>
        Assert.Empty(TransactionValidator.ValidateAgainstAccount(1200m, Msg(-1200m)));

    [Fact]
    public void Zero_dollar_cancellation_books_zero_commission() =>
        // A legit $0 transaction (e.g. cancelling a policy already reduced to $0) must not book a stray commission.
        Assert.Equal(0m, CommissionCalculator.Cumulative(0m, 0m, 0m, 0.15m));
}
