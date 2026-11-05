using PremiumSync.PolicyService.Domain;

namespace PremiumSync.Tests;

public class PremiumMathTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2027, 1, 1);   // 365-day term

    [Fact]
    public void Full_term_is_full_amount() =>
        Assert.Equal(1200.00m, PremiumMath.ProRata(1200m, Start, End, Start));

    [Fact]
    public void Expiration_day_is_zero() =>
        Assert.Equal(0m, PremiumMath.ProRata(1200m, Start, End, End));

    [Fact]
    public void Midterm_is_prorated_by_days_remaining()
    {
        // 2026-07-02 leaves 183 of 365 days: 1200 * 183 / 365 = 601.643... -> 601.64
        Assert.Equal(601.64m, PremiumMath.ProRata(1200m, Start, End, new DateOnly(2026, 7, 2)));
    }

    [Fact]
    public void Negative_change_prorates_to_negative() =>
        Assert.Equal(-601.64m, PremiumMath.ProRata(-1200m, Start, End, new DateOnly(2026, 7, 2)));

    // ---------- Cancellation ----------
    // Scenario used below: $1,200 bound 1/1, +$600 annual endorsed 7/2 (prorated to $300.82).

    private static readonly DateOnly EndorseDate = new(2026, 7, 2);
    private static readonly (decimal, DateOnly)[] Endorsed = [(1200m, Start), (300.82m, EndorseDate)];

    [Fact]
    public void Unendorsed_policy_returns_unearned_share() =>
        // 92 of 365 days left: 1200 x 92 / 365 = 302.465... -> 302.47
        Assert.Equal(-302.47m, PremiumMath.CancellationReturn([(1200m, Start)], End, new DateOnly(2026, 10, 1)));

    [Fact]
    public void Endorsed_policy_returns_each_transactions_own_share()
    {
        // New business: 1200 x 92/365 = 302.4658. Endorsement covered 183 days: 300.82 x 92/183 = 151.2334.
        // Together 453.6992 -> 453.70. The old math (1500.82 x 92/365) returned only 378.29.
        Assert.Equal(-453.70m, PremiumMath.CancellationReturn(Endorsed, End, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Flat_cancel_returns_everything() =>
        Assert.Equal(-1500.82m, PremiumMath.CancellationReturn(Endorsed, End, Start));

    [Fact]
    public void Cancel_before_an_endorsement_takes_effect_returns_all_of_that_endorsement()
    {
        // Cancel 6/1: new business returns 1200 x 214/365 = 703.5616; the 7/2 endorsement never took effect, so all 300.82.
        Assert.Equal(-1004.38m, PremiumMath.CancellationReturn(Endorsed, End, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Cancel_on_expiration_returns_nothing() =>
        Assert.Equal(0m, PremiumMath.CancellationReturn(Endorsed, End, End));

    [Fact]
    public void Return_premium_from_an_earlier_reduction_is_handled()
    {
        // A -$600 annual reduction on 7/2 prorates to -300.82; cancelling 10/1 gives back its unearned share as a charge.
        var reduced = new (decimal, DateOnly)[] { (1200m, Start), (-300.82m, EndorseDate) };
        // 302.4658 - 151.2334 = 151.2324 -> 151.23
        Assert.Equal(-151.23m, PremiumMath.CancellationReturn(reduced, End, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Effective_date_outside_term_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PremiumMath.ProRata(1200m, Start, End, new DateOnly(2027, 2, 1)));
}
