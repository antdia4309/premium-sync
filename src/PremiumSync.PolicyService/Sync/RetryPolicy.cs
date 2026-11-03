namespace PremiumSync.PolicyService.Sync;

public class RetryOptions
{
    public int MaxAttempts { get; set; } = 6;          // including the first send
    public int BaseDelaySeconds { get; set; } = 30;    // wait after the first failure
    public int MaxDelaySeconds { get; set; } = 3600;   // never wait longer than this
    public int PollSeconds { get; set; } = 15;         // how often the worker checks for due retries
    public int BatchSize { get; set; } = 50;
}

/// <summary>
/// Exponential backoff: each failure doubles the wait (30s, 60s, 120s...), capped at MaxDelaySeconds.
/// Backing off gives a struggling system room to recover instead of hammering it.
/// </summary>
public static class RetryPolicy
{
    /// <summary>When to try again after the given number of attempts, or null if it's time to give up.</summary>
    public static DateTime? NextAttemptAt(int attemptsSoFar, DateTime nowUtc, RetryOptions o)
    {
        if (attemptsSoFar >= o.MaxAttempts) return null;
        var delay = Delay(attemptsSoFar, o);
        return nowUtc + delay;
    }

    public static TimeSpan Delay(int attemptsSoFar, RetryOptions o)
    {
        var exponent = Math.Clamp(attemptsSoFar - 1, 0, 30);   // clamp so 2^n can't overflow
        var seconds = Math.Min(o.BaseDelaySeconds * Math.Pow(2, exponent), o.MaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
