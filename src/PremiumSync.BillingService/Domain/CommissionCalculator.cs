namespace PremiumSync.BillingService.Domain;

public static class CommissionCalculator
{
    /// <summary>
    /// Commission follows premium in both directions: a return premium produces a negative commission.
    /// Rounded to cents, half away from zero.
    /// </summary>
    public static decimal Calculate(decimal premium, decimal rate) =>
        Math.Round(premium * rate, 2, MidpointRounding.AwayFromZero);
}
