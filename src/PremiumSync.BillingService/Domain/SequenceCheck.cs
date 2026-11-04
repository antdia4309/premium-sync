namespace PremiumSync.BillingService.Domain;

public enum SequenceResult
{
    InOrder,      // exactly the next one expected: book it
    TooEarly,     // skips ahead (e.g. 3 arrives before 2): try again later
    AlreadyUsed   // this sequence is already booked under a different TransactionId: reject
}

/// <summary>
/// Billing books each policy's transactions strictly in order: 1, 2, 3...
/// Runs after the idempotency check, so a retry of an already-booked transaction never gets here.
/// </summary>
public static class SequenceCheck
{
    public static SequenceResult Evaluate(int? lastBookedSequence, int incomingSequence)
    {
        var expected = (lastBookedSequence ?? 0) + 1;
        if (incomingSequence == expected) return SequenceResult.InOrder;
        return incomingSequence > expected ? SequenceResult.TooEarly : SequenceResult.AlreadyUsed;
    }

    public static int Expected(int? lastBookedSequence) => (lastBookedSequence ?? 0) + 1;
}
