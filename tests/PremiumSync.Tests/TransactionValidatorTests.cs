using PremiumSync.BillingService.Domain;
using PremiumSync.Contracts;

namespace PremiumSync.Tests;

public class TransactionValidatorTests
{
    private static PolicyTransactionMessage Valid(TransactionType type = TransactionType.NewBusiness, int seq = 1, decimal premium = 1000m) =>
        new(Guid.NewGuid(), "PS-000001", seq, type, new DateOnly(2026, 3, 1), premium, 25m, 0.15m);

    [Fact]
    public void Valid_new_business_passes() => Assert.Empty(TransactionValidator.Validate(Valid()));

    [Fact]
    public void Valid_cancellation_passes() =>
        Assert.Empty(TransactionValidator.Validate(Valid(TransactionType.Cancellation, seq: 3, premium: -400m)));

    [Fact]
    public void Positive_cancellation_premium_is_rejected()
    {
        var errors = TransactionValidator.Validate(Valid(TransactionType.Cancellation, seq: 2, premium: 400m));
        Assert.Contains(nameof(PolicyTransactionMessage.PremiumChange), errors.Keys);
    }

    [Fact]
    public void New_business_must_be_sequence_one()
    {
        var errors = TransactionValidator.Validate(Valid(seq: 2));
        Assert.Contains(nameof(PolicyTransactionMessage.Sequence), errors.Keys);
    }

    [Fact]
    public void Endorsement_cannot_be_sequence_one()
    {
        var errors = TransactionValidator.Validate(Valid(TransactionType.Endorsement, seq: 1, premium: 100m));
        Assert.Contains(nameof(PolicyTransactionMessage.Sequence), errors.Keys);
    }

    [Fact]
    public void Fractional_cents_are_rejected()
    {
        var errors = TransactionValidator.Validate(Valid(premium: 1000.005m));
        Assert.Contains(nameof(PolicyTransactionMessage.PremiumChange), errors.Keys);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.51)]
    public void Commission_rate_out_of_range_is_rejected(decimal rate)
    {
        var errors = TransactionValidator.Validate(Valid() with { CommissionRate = rate });
        Assert.Contains(nameof(PolicyTransactionMessage.CommissionRate), errors.Keys);
    }

    [Fact]
    public void Missing_ids_are_rejected()
    {
        var errors = TransactionValidator.Validate(Valid() with { TransactionId = Guid.Empty, PolicyNumber = " " });
        Assert.Contains(nameof(PolicyTransactionMessage.TransactionId), errors.Keys);
        Assert.Contains(nameof(PolicyTransactionMessage.PolicyNumber), errors.Keys);
    }
}
