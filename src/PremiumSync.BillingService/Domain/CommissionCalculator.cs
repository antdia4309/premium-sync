namespace PremiumSync.BillingService.Domain;

public static class CommissionCalculator
{
    /// <summary>
    /// Commission on one premium amount. Follows premium in both directions:
    /// a return premium produces a negative commission. Rounded to cents, half away from zero.
    /// </summary>
    public static decimal Calculate(decimal premium, decimal rate) =>
        Math.Round(premium * rate, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Commission for a new transaction, calculated cumulatively so rounding never drifts:
    /// (commission on ALL premium to date, including this transaction) minus (commission already booked).
    /// Each transaction can be off by a fraction of a cent on its own, but the running total is always exact.
    /// </summary>
    public static decimal Cumulative(decimal premiumBooked, decimal commissionBooked, decimal incomingPremium, decimal rate) =>
        Calculate(premiumBooked + incomingPremium, rate) - commissionBooked;
}
