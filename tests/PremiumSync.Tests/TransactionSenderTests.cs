using System.Net;
using Microsoft.Extensions.Options;
using PremiumSync.Contracts;
using PremiumSync.PolicyService.Billing;
using PremiumSync.PolicyService.Data;
using PremiumSync.PolicyService.Sync;

namespace PremiumSync.Tests;

public class TransactionSenderTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RetryOptions Opts = new() { MaxAttempts = 3, BaseDelaySeconds = 10, MaxDelaySeconds = 60 };

    /// <summary>Stands in for the billing service: answers every request with a canned response.</summary>
    private class FakeBilling(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    private class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private static TransactionSender SenderReturning(Func<HttpResponseMessage> respond) =>
        new(new BillingClient(new HttpClient(new FakeBilling(respond)) { BaseAddress = new Uri("http://billing") }),
            Options.Create(Opts), new FixedClock(Now));

    private static TransactionSender SenderThatCannotConnect() =>
        SenderReturning(() => throw new HttpRequestException("Connection refused"));

    private static PolicyTransaction NewTxn(int attemptsSoFar = 0) => new()
    {
        Policy = new Policy { PolicyNumber = "PS-000001", CommissionRate = 0.15m },
        Sequence = 1, Type = TransactionType.NewBusiness, EffectiveDate = new DateOnly(2026, 1, 1),
        PremiumChange = 1200m, AttemptCount = attemptsSoFar,
    };

    [Fact]
    public async Task Success_marks_sent_and_clears_retry()
    {
        var txn = NewTxn();
        await SenderReturning(() => new HttpResponseMessage(HttpStatusCode.Created)).SendAsync(txn);

        Assert.Equal(SyncStatus.Sent, txn.SyncStatus);
        Assert.Equal(1, txn.AttemptCount);
        Assert.Null(txn.NextAttemptAtUtc);
        Assert.Null(txn.SyncError);
    }

    [Fact]
    public async Task Billing_down_schedules_a_retry()
    {
        var txn = NewTxn();
        await SenderThatCannotConnect().SendAsync(txn);

        Assert.Equal(SyncStatus.Failed, txn.SyncStatus);
        Assert.Equal(Now.AddSeconds(10), txn.NextAttemptAtUtc);
        Assert.Contains("unreachable", txn.SyncError);
    }

    [Fact]
    public async Task Validation_rejection_goes_straight_to_error_queue()
    {
        var txn = NewTxn();
        await SenderReturning(() => new HttpResponseMessage(HttpStatusCode.BadRequest)).SendAsync(txn);

        Assert.Equal(SyncStatus.DeadLettered, txn.SyncStatus);
        Assert.Null(txn.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Running_out_of_attempts_goes_to_error_queue()
    {
        var txn = NewTxn(attemptsSoFar: 2);   // this send is attempt 3 of 3
        await SenderThatCannotConnect().SendAsync(txn);

        Assert.Equal(SyncStatus.DeadLettered, txn.SyncStatus);
        Assert.Equal(3, txn.AttemptCount);
    }
}
