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
    /// Simplification: applies the remaining-term fraction to total written premium.
    /// That's exact when there are no endorsements; Phase 2 is a good time to make it exact for every case.
    /// </summary>
    public static decimal CancellationReturn(decimal writtenPremium, DateOnly termStart, DateOnly termEnd, DateOnly cancelDate)
        => -ProRata(writtenPremium, termStart, termEnd, cancelDate);
}
