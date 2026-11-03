using PremiumSync.BillingService.Data;
using PremiumSync.BillingService.Domain;
using PremiumSync.Contracts;

namespace PremiumSync.Tests;

public class ReplayCheckTests
{
    private static readonly PolicyTransactionMessage Msg =
        new(Guid.NewGuid(), "PS-000001", 1, TransactionType.NewBusiness, new DateOnly(2026, 1, 1), 1200m, 50m, 0.15m);

    private static BillingTransaction BookedFrom(PolicyTransactionMessage m) => new()
    {
        SourceTransactionId = m.TransactionId, PolicyNumber = m.PolicyNumber, Sequence = m.Sequence, Type = m.Type,
        EffectiveDate = m.EffectiveDate, Premium = m.PremiumChange, FeeAmount = m.FeeAmount, CommissionRate = m.CommissionRate,
    };

    [Fact]
    public void Unseen_transaction_is_new() =>
        Assert.Equal(ReplayResult.New, ReplayCheck.Evaluate(null, Msg));

    [Fact]
    public void Same_transaction_sent_again_is_a_duplicate() =>
        Assert.Equal(ReplayResult.Duplicate, ReplayCheck.Evaluate(BookedFrom(Msg), Msg));

    [Fact]
    public void Same_id_with_different_premium_is_a_conflict() =>
        Assert.Equal(ReplayResult.Conflict, ReplayCheck.Evaluate(BookedFrom(Msg), Msg with { PremiumChange = 1300m }));

    [Fact]
    public void Same_id_on_a_different_policy_is_a_conflict() =>
        Assert.Equal(ReplayResult.Conflict, ReplayCheck.Evaluate(BookedFrom(Msg), Msg with { PolicyNumber = "PS-000002" }));
}
