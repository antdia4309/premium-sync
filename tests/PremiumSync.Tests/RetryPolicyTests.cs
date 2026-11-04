using PremiumSync.Contracts;
using System.Net;
using PremiumSync.PolicyService.Billing;
using PremiumSync.PolicyService.Sync;

namespace PremiumSync.Tests;

public class RetryPolicyTests
{
    private static readonly RetryOptions Opts = new() { MaxAttempts = 5, BaseDelaySeconds = 30, MaxDelaySeconds = 300 };
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 30)]    // after the 1st failure, wait 30s
    [InlineData(2, 60)]    // then double it
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    public void Delay_doubles_after_each_failure(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.Delay(attempts, Opts));

    [Fact]
    public void Delay_is_capped() =>
        Assert.Equal(TimeSpan.FromSeconds(300), RetryPolicy.Delay(10, Opts));

    [Fact]
    public void Huge_attempt_counts_do_not_overflow() =>
        Assert.Equal(TimeSpan.FromSeconds(300), RetryPolicy.Delay(int.MaxValue, Opts));

    [Fact]
    public void Next_attempt_is_now_plus_delay() =>
        Assert.Equal(Now.AddSeconds(60), RetryPolicy.NextAttemptAt(2, Now, Opts));

    [Fact]
    public void Gives_up_after_max_attempts() =>
        Assert.Null(RetryPolicy.NextAttemptAt(5, Now, Opts));

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData((HttpStatusCode)ContractStatusCodes.OutOfSequence, true)]      // out of sequence: the earlier one may still arrive
    [InlineData(HttpStatusCode.BadRequest, false)]   // billing rejected the data
    [InlineData(HttpStatusCode.Conflict, false)]     // same TransactionId, different data
    [InlineData(HttpStatusCode.NotFound, false)]
    public void Only_temporary_errors_are_retried(HttpStatusCode status, bool expected) =>
        Assert.Equal(expected, BillingClient.IsRetryable(status));
}
