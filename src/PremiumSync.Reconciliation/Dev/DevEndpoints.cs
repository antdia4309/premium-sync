using Microsoft.Extensions.Options;
using PremiumSync.Reconciliation.Data;
using PremiumSync.Reconciliation.Engine;

namespace PremiumSync.Reconciliation.Dev;

/// <summary>Only mapped in Development. These endpoints corrupt data on purpose.</summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/dev").WithTags("Dev");

        g.MapGet("/inject/kinds", () => Results.Ok(DiscrepancyInjector.Kinds.Select(k => k.ToString())))
            .WithSummary("Discrepancy kinds that can be injected");

        g.MapPost("/inject", (IOptions<ReconOptions> o, IHostEnvironment env, string kind, string? policy) =>
        {
            if (!Enum.TryParse<ReasonCode>(kind, ignoreCase: true, out var code) || !DiscrepancyInjector.Kinds.Contains(code))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["kind"] = [$"Use one of: {string.Join(", ", DiscrepancyInjector.Kinds)}."]
                });
            return Guard(() => Results.Ok(DiscrepancyInjector.Inject(Path(env, o.Value.PolicyDb), Path(env, o.Value.BillingDb), code, policy)));
        }).WithSummary("Corrupt the data with one discrepancy (optionally on a specific policy)");

        g.MapPost("/inject/all", (IOptions<ReconOptions> o, IHostEnvironment env) =>
            Guard(() => Results.Ok(DiscrepancyInjector.InjectAll(Path(env, o.Value.PolicyDb), Path(env, o.Value.BillingDb)))))
            .WithSummary("Inject one of every discrepancy kind, each on a different policy");
    }

    private static IResult Guard(Func<IResult> action)
    {
        try { return action(); }
        catch (FileNotFoundException ex) { return Results.Problem(statusCode: 503, title: "Source database missing", detail: ex.Message); }
        catch (InvalidOperationException ex) { return Results.Problem(statusCode: 409, title: "Can't inject", detail: ex.Message); }
    }

    private static string Path(IHostEnvironment env, string p) => System.IO.Path.GetFullPath(System.IO.Path.Combine(env.ContentRootPath, p));
}
