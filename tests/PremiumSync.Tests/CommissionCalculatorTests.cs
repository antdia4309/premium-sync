using PremiumSync.BillingService.Domain;

namespace PremiumSync.Tests;

public class CommissionCalculatorTests
{
    [Theory]
    [InlineData(1000.00, 0.15, 150.00)]
    [InlineData(1234.56, 0.125, 154.32)]   // 154.32 exactly
    [InlineData(0.10, 0.15, 0.02)]         // 0.015 rounds away from zero to 0.02
    [InlineData(-600.00, 0.15, -90.00)]    // return premium = negative commission
    [InlineData(-0.10, 0.15, -0.02)]       // rounds away from zero on the negative side too
    [InlineData(500.00, 0.0, 0.00)]
    public void Calculates_commission_rounded_to_cents(decimal premium, decimal rate, decimal expected)
    {
        Assert.Equal(expected, CommissionCalculator.Calculate(premium, rate));
    }
}
