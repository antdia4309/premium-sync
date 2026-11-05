using PremiumSync.BillingService.Domain;

namespace PremiumSync.Tests;

public class CumulativeCommissionTests
{
    [Fact]
    public void First_transaction_matches_simple_calculation() =>
        Assert.Equal(CommissionCalculator.Calculate(1234.56m, 0.125m), CommissionCalculator.Cumulative(0m, 0m, 1234.56m, 0.125m));

    [Fact]
    public void Per_transaction_rounding_drifts()
    {
        // The bug, written as a test: $1,000 + twenty $1.00 endorsements at 12.5%.
        // Each $1.00 is exactly 12.5 cents, a half cent, so it rounds up to 13 every time.
        var total = CommissionCalculator.Calculate(1000m, 0.125m)
                  + Enumerable.Range(0, 20).Sum(_ => CommissionCalculator.Calculate(1m, 0.125m));
        Assert.Equal(127.60m, total);   // should be 1,020 x 12.5% = 127.50
    }

    [Fact]
    public void Cumulative_commission_does_not_drift()
    {
        decimal premium = 0m, commission = 0m;
        void Book(decimal p)
        {
            commission += CommissionCalculator.Cumulative(premium, commission, p, 0.125m);
            premium += p;
        }

        Book(1000m);
        for (var i = 0; i < 20; i++) Book(1m);

        Assert.Equal(1020m, premium);
        Assert.Equal(127.50m, commission);
    }

    [Fact]
    public void Individual_amounts_alternate_to_keep_the_total_exact()
    {
        // After $1,000 (125.00), two $1.00 endorsements: 125.13 - 125.00 = 0.13, then 125.25 - 125.13 = 0.12.
        Assert.Equal(0.13m, CommissionCalculator.Cumulative(1000m, 125.00m, 1m, 0.125m));
        Assert.Equal(0.12m, CommissionCalculator.Cumulative(1001m, 125.13m, 1m, 0.125m));
    }

    [Fact]
    public void Full_cancellation_brings_commission_back_to_zero()
    {
        // 1,020 booked with 127.50 commission; cancelling all of it returns exactly 127.50.
        Assert.Equal(-127.50m, CommissionCalculator.Cumulative(1020m, 127.50m, -1020m, 0.125m));
    }
}
