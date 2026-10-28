using Microsoft.EntityFrameworkCore;
using PremiumSync.PolicyService.Data;
using PremiumSync.PolicyService.Domain;

namespace PremiumSync.PolicyService.Endpoints;

public static class PolicyEndpoints
{
    public static void MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/policies").WithTags("Policies");

        g.MapGet("/", async (PolicyDbContext db, int take = 50) =>
        {
            var policies = await db.Policies.Include(p => p.Transactions)
                .OrderByDescending(p => p.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync();
            return TypedResults.Ok(policies.Select(PolicyDto.From));
        }).WithSummary("List policies, newest first");

        g.MapGet("/{policyNumber}", async (string policyNumber, PolicyDbContext db) =>
        {
            var p = await db.Policies.Include(x => x.Transactions).FirstOrDefaultAsync(x => x.PolicyNumber == policyNumber);
            return p is null ? Results.NotFound() : Results.Ok(PolicyDto.From(p));
        }).WithSummary("Get one policy with its transactions");

        g.MapPost("/", async (CreatePolicyRequest req, PolicyWorkflow wf) =>
            ToHttp(await wf.CreateAsync(req), created: true))
            .WithSummary("Bind a new policy (sends a NewBusiness transaction to billing)");

        g.MapPost("/{policyNumber}/endorsements", async (string policyNumber, EndorseRequest req, PolicyWorkflow wf) =>
            ToHttp(await wf.EndorseAsync(policyNumber, req)))
            .WithSummary("Endorse a policy (annual change is prorated)");

        g.MapPost("/{policyNumber}/cancel", async (string policyNumber, CancelRequest req, PolicyWorkflow wf) =>
            ToHttp(await wf.CancelAsync(policyNumber, req)))
            .WithSummary("Cancel a policy pro rata");
    }

    private static IResult ToHttp(WorkflowResult r, bool created = false)
    {
        if (r.NotFound) return Results.NotFound();
        if (r.Errors is not null) return Results.ValidationProblem(r.Errors);

        var policy = r.Transaction!.Policy!;
        var dto = PolicyDto.From(policy);
        return created ? Results.Created($"/api/policies/{policy.PolicyNumber}", dto) : Results.Ok(dto);
    }
}
