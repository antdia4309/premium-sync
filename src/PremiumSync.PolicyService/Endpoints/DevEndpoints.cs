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
    }
}
