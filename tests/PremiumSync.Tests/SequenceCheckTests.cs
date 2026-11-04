using PremiumSync.BillingService.Domain;

namespace PremiumSync.Tests;

public class SequenceCheckTests
{
    [Fact]
    public void First_transaction_must_be_sequence_one() =>
        Assert.Equal(SequenceResult.InOrder, SequenceCheck.Evaluate(null, 1));

    [Fact]
    public void Next_sequence_is_in_order() =>
        Assert.Equal(SequenceResult.InOrder, SequenceCheck.Evaluate(2, 3));

    [Theory]
    [InlineData(null, 2)]   // endorsement before new business
    [InlineData(1, 3)]      // cancellation before the endorsement
    public void Skipping_ahead_is_too_early(int? lastBooked, int incoming) =>
        Assert.Equal(SequenceResult.TooEarly, SequenceCheck.Evaluate(lastBooked, incoming));

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    public void Reusing_a_booked_sequence_is_rejected(int lastBooked, int incoming) =>
        Assert.Equal(SequenceResult.AlreadyUsed, SequenceCheck.Evaluate(lastBooked, incoming));

    [Fact]
    public void Expected_is_one_more_than_last_booked()
    {
        Assert.Equal(1, SequenceCheck.Expected(null));
        Assert.Equal(4, SequenceCheck.Expected(3));
    }
}
