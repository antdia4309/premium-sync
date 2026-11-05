using PremiumSync.PolicyService.Billing;
using PremiumSync.PolicyService.Domain;

namespace PremiumSync.PolicyService.Endpoints;

/// <summary>Generates realistic-looking test data. Only mapped in Development.</summary>
public static class DevEndpoints
{
    private static readonly string[] Names =
        ["Riverside Dental Group", "Blue Ridge Consulting", "Harbor Point Logistics", "Summit Architecture",
         "Oakwood Property Mgmt", "Northstar IT Services", "Cedar Lane Accounting", "Pioneer Engineering",
         "Lakeside Medical Billing", "Granite Legal Partners", "Maple Street Realty", "Ironclad Security"];

    private static readonly decimal[] Rates = [0.10m, 0.125m, 0.15m, 0.175m, 0.20m];

    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/seed", async (PolicyWorkflow wf, int count = 50, int seed = 42) =>
        {
            var rng = new Random(seed);
            int policies = 0, endorsements = 0, cancellations = 0, failed = 0;

            for (var i = 0; i < Math.Clamp(count, 1, 1000); i++)
            {
                var eff = new DateOnly(2026, 1, 1).AddDays(rng.Next(0, 300));
                var created = await wf.CreateAsync(new CreatePolicyRequest(
                    $"{Names[rng.Next(Names.Length)]} #{i + 1}",
                    eff,
                    AnnualPremium: rng.Next(5, 250) * 100m,
                    FeeAmount: rng.Next(0, 4) * 25m,
                    CommissionRate: Rates[rng.Next(Rates.Length)]));
                policies++;
                var policyNumber = created.Transaction!.Policy!.PolicyNumber;
                if (created.Transaction.SyncError is not null) failed++;

                if (rng.NextDouble() < 0.40)
                {
                    var r = await wf.EndorseAsync(policyNumber, new EndorseRequest(
                        eff.AddDays(rng.Next(15, 200)), AnnualPremiumChange: rng.Next(-20, 40) * 50m));
                    endorsements++;
                    if (r.Transaction?.SyncError is not null) failed++;
                }

                if (rng.NextDouble() < 0.15)
                {
                    var r = await wf.CancelAsync(policyNumber, new CancelRequest(eff.AddDays(rng.Next(201, 330))));
                    cancellations++;
                    if (r.Transaction?.SyncError is not null) failed++;
                }
            }

            return Results.Ok(new { policies, endorsements, cancellations, failedToSend = failed });
        }).WithTags("Dev").WithSummary("Generate test policies, endorsements, and cancellations");

        app.MapPost("/api/dev/drift-demo", async (PolicyWorkflow wf, BillingClient billing, int endorsements = 20) =>
        {
            // $1,000 at 12.5%, then N endorsements of +$1.00 effective on day one (so each is exactly $1.00).
            // $1.00 x 12.5% = 12.5 cents, a perfect half-cent: per-transaction rounding pushes every one up to 13.
            var eff = new DateOnly(2026, 1, 1);
            var created = await wf.CreateAsync(new CreatePolicyRequest("Drift Demo LLC", eff, 1000m, 0m, 0.125m));
            var policyNumber = created.Transaction!.Policy!.PolicyNumber;

            for (var i = 0; i < Math.Clamp(endorsements, 1, 200); i++)
                await wf.EndorseAsync(policyNumber, new EndorseRequest(eff, AnnualPremiumChange: 1m));

            var policy = PolicyDto.From(created.Transaction.Policy);
            var totals = await billing.GetTotalsAsync(policyNumber);
            return Results.Ok(new
            {
                policyNumber,
                policyWrittenPremium = policy.WrittenPremium,
                policyExpectedCommission = policy.ExpectedCommission,
                billingPremium = totals?.TotalPremium,
                billingCommission = totals?.TotalCommission,
                commissionDrift = totals is null ? (decimal?)null : totals.TotalCommission - policy.ExpectedCommission,
            });
        }).WithTags("Dev").WithSummary("One policy with many small endorsements: compare policy vs. billing commission");
    }
}
