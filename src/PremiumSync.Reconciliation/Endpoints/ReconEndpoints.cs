using Microsoft.EntityFrameworkCore;
using PremiumSync.Reconciliation.Data;
using PremiumSync.Reconciliation.Engine;

namespace PremiumSync.Reconciliation.Endpoints;

public record RunSummary(int RunId, DateTime StartedAtUtc, DateTime CompletedAtUtc, DateTime CutoffUtc,
    int PoliciesChecked, int TransactionsChecked, int FindingCount, Dictionary<string, int> ByReason);

public record FindingDto(string ReasonCode, string PolicyNumber, Guid? TransactionId, int? Sequence,
    decimal? PolicyValue, decimal? BillingValue, decimal? Difference, string Detail);

public static class ReconEndpoints
{
    public static void MapReconEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/recon").WithTags("Reconciliation");

        g.MapPost("/runs", async (ReconService recon, ReconDbContext db) =>
        {
            try
            {
                var run = await recon.RunAsync();
                return Results.Created($"/api/recon/runs/{run.Id}", await SummaryAsync(db, run));
            }
            catch (FileNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Source database missing", detail: ex.Message);
            }
        }).WithSummary("Run a reconciliation now and save the results");

        g.MapGet("/runs", async (ReconDbContext db, int take = 20) =>
        {
            var runs = await db.Runs.OrderByDescending(r => r.Id).Take(Math.Clamp(take, 1, 200)).ToListAsync();
            var summaries = new List<RunSummary>();
            foreach (var run in runs) summaries.Add(await SummaryAsync(db, run));
            return Results.Ok(summaries);
        }).WithSummary("Recent runs with counts by reason code");

        g.MapGet("/runs/latest", async (ReconDbContext db) =>
        {
            var run = await db.Runs.OrderByDescending(r => r.Id).FirstOrDefaultAsync();
            return run is null ? Results.NotFound() : Results.Ok(await SummaryAsync(db, run));
        }).WithSummary("Most recent run");

        g.MapGet("/runs/{runId:int}/findings", async (int runId, ReconDbContext db, string? reason, string? policy) =>
        {
            if (!await db.Runs.AnyAsync(r => r.Id == runId)) return Results.NotFound();

            var q = db.Findings.Where(f => f.RunId == runId);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                if (!Enum.TryParse<ReasonCode>(reason, ignoreCase: true, out var code))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["reason"] = [$"Unknown reason code. Use one of: {string.Join(", ", Enum.GetNames<ReasonCode>())}."]
                    });
                q = q.Where(f => f.ReasonCode == code);
            }
            if (!string.IsNullOrWhiteSpace(policy)) q = q.Where(f => f.PolicyNumber == policy);

            var rows = await q.OrderBy(f => f.PolicyNumber).ThenBy(f => f.Sequence).ThenBy(f => f.ReasonCode).ToListAsync();
            return Results.Ok(rows.Select(f => new FindingDto(f.ReasonCode.ToString(), f.PolicyNumber, f.TransactionId, f.Sequence,
                f.PolicyValue, f.BillingValue, f.Difference, f.Detail)));
        }).WithSummary("Findings for a run, optionally filtered by reason code or policy number");
    }

    private static async Task<RunSummary> SummaryAsync(ReconDbContext db, ReconRun run)
    {
        var counts = await db.Findings.Where(f => f.RunId == run.Id)
            .GroupBy(f => f.ReasonCode).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var byReason = Enum.GetValues<ReasonCode>().ToDictionary(c => c.ToString(), c => counts.FirstOrDefault(x => x.Key == c)?.Count ?? 0);
        return new RunSummary(run.Id, run.StartedAtUtc, run.CompletedAtUtc, run.CutoffUtc,
            run.PoliciesChecked, run.TransactionsChecked, run.FindingCount, byReason);
    }
}
