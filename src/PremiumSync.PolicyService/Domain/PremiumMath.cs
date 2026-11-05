namespace PremiumSync.PolicyService.Domain;

public static class PremiumMath
{
    /// <summary>
    /// Prorates an annual amount from the effective date to the end of the term.
    /// Example: $1,200 annual change effective halfway through the term = $600.
    /// </summary>
    public static decimal ProRata(decimal annualAmount, DateOnly termStart, DateOnly termEnd, DateOnly effective)
    {
        if (termEnd <= termStart) throw new ArgumentException("Term end must be after term start.");
        if (effective < termStart || effective > termEnd) throw new ArgumentOutOfRangeException(nameof(effective), "Effective date must fall within the term.");

        decimal termDays = termEnd.DayNumber - termStart.DayNumber;
        decimal remainingDays = termEnd.DayNumber - effective.DayNumber;
        return Math.Round(annualAmount * remainingDays / termDays, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Return premium on a pro-rata cancellation, as a negative number.
    /// Each transaction covers its own slice of the term (from its effective date to expiration),
    /// so each one returns its own unearned share:
    ///   - cancel on or before a transaction's effective date: return all of it
    ///   - cancel after: return premium x (days left after cancel / days that transaction covered)
    /// The shares are added up unrounded and rounded once, so rounding can't stack up across transactions.
    /// </summary>
    public static decimal CancellationReturn(IEnumerable<(decimal Premium, DateOnly Effective)> transactions,
        DateOnly termEnd, DateOnly cancelDate)
    {
        decimal unearned = 0m;
        foreach (var (premium, effective) in transactions)
        {
            if (cancelDate <= effective)
            {
                unearned += premium;
                continue;
            }

            decimal coveredDays = termEnd.DayNumber - effective.DayNumber;
            if (coveredDays <= 0) continue;   // effective on the expiration date: nothing left to return
            decimal remainingDays = Math.Max(0, termEnd.DayNumber - cancelDate.DayNumber);
            unearned += premium * remainingDays / coveredDays;
        }

        return -Math.Round(unearned, 2, MidpointRounding.AwayFromZero);
    }
}
