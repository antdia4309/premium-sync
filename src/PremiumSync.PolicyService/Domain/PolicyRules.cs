using System.Globalization;
using PremiumSync.PolicyService.Data;

namespace PremiumSync.PolicyService.Domain;

/// <summary>
/// Business rules checked before a transaction is created. Each returns an error message, or null if the change is allowed.
/// Kept separate from PolicyWorkflow so they're easy to unit test without a database.
/// </summary>
public static class PolicyRules
{
    /// <summary>
    /// Endorsements and cancellations must take effect on or after the policy's effective date and BEFORE expiration.
    /// On the expiration date the policy is fully earned: a change there covers zero days and can only produce a $0 transaction.
    /// </summary>
    public static string? CheckChangeDate(Policy p, DateOnly effective, string action)
    {
        if (effective < p.EffectiveDate)
            return $"{action} can't take effect before the policy's effective date ({p.EffectiveDate:yyyy-MM-dd}).";
        if (effective >= p.ExpirationDate)
            return $"{action} must take effect before the policy expires on {p.ExpirationDate:yyyy-MM-dd}. On or after that date the policy is fully earned.";
        return null;
    }

    /// <summary>
    /// An endorsement can lower premium, but never below $0.00. Negative written premium means paying back more than was charged,
    /// clawing back more commission than was paid, and a cancellation that would CHARGE the insured instead of refunding them.
    /// </summary>
    public static string? CheckWrittenPremium(decimal currentWritten, decimal proratedChange)
    {
        var after = currentWritten + proratedChange;
        return after < 0
            ? $"This endorsement would reduce written premium to {Money(after)} (currently {Money(currentWritten)}, change {Money(proratedChange)}). Written premium can't go below $0.00."
            : null;
    }

    /// <summary>Dollars with the sign in front: -$800.00. Culture-independent so messages read the same on any server.</summary>
    private static string Money(decimal d) =>
        (d < 0 ? "-$" : "$") + Math.Abs(d).ToString("N2", CultureInfo.InvariantCulture);
}
