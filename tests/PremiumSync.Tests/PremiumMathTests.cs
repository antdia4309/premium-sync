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

    [Fact]
    public void Cancellation_returns_unearned_premium_as_negative() =>
        Assert.Equal(-601.64m, PremiumMath.CancellationReturn(1200m, Start, End, new DateOnly(2026, 7, 2)));

    [Fact]
    public void Effective_date_outside_term_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PremiumMath.ProRata(1200m, Start, End, new DateOnly(2027, 2, 1)));
}
