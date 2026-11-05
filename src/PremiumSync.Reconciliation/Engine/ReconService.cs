using Microsoft.Extensions.Options;
using PremiumSync.Reconciliation.Data;

namespace PremiumSync.Reconciliation.Engine;

public class ReconOptions
{
    public string PolicyDb { get; set; } = "../PremiumSync.PolicyService/data/policy.db";
    public string BillingDb { get; set; } = "../PremiumSync.BillingService/data/billing.db";
    /// <summary>Undelivered transactions younger than this are "in flight" and not reported.</summary>
    public int StuckAfterMinutes { get; set; } = 30;
}

/// <summary>Runs the engine against the configured databases and saves the results.</summary>
public class ReconService(ReconDbContext db, IOptions<ReconOptions> options, IHostEnvironment env, TimeProvider clock)
{
    public async Task<ReconRun> RunAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var started = clock.GetUtcNow().UtcDateTime;
        var cutoff = started.AddMinutes(-o.StuckAfterMinutes);

        var result = ReconEngine.Run(Resolve(o.PolicyDb), Resolve(o.BillingDb), cutoff);

        var run = new ReconRun
        {
            StartedAtUtc = started,
            CompletedAtUtc = clock.GetUtcNow().UtcDateTime,
            CutoffUtc = cutoff,
            PoliciesChecked = result.PoliciesChecked,
            TransactionsChecked = result.TransactionsChecked,
            FindingCount = result.Findings.Count,
            Findings = result.Findings,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    // Paths in config are relative to this project's folder.
    private string Resolve(string path) => Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
}
