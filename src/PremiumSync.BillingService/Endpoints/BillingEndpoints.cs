using Microsoft.EntityFrameworkCore;
using PremiumSync.BillingService.Data;
using PremiumSync.BillingService.Domain;
using PremiumSync.Contracts;

namespace PremiumSync.BillingService.Endpoints;

public record AccountSummary(string PolicyNumber, decimal TotalPremium, decimal TotalFees,
    decimal TotalCommission, int TransactionCount, List<BillingTransaction> Transactions);

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        // The integration entry point: the policy system posts here.
        app.MapPost("/api/inbound/transactions", async (PolicyTransactionMessage msg, BillingDbContext db, ILogger<BillingTransaction> log) =>
        {
            var errors = TransactionValidator.Validate(msg);
            if (errors.Count > 0)
            {
                log.LogWarning("Rejected {TransactionId} for {PolicyNumber}: {Errors}", msg.TransactionId, msg.PolicyNumber, string.Join("; ", errors.SelectMany(e => e.Value)));
                return Results.ValidationProblem(errors);
            }

            var row = new BillingTransaction
            {
                SourceTransactionId = msg.TransactionId,
                PolicyNumber = msg.PolicyNumber,
                Sequence = msg.Sequence,
                Type = msg.Type,
                EffectiveDate = msg.EffectiveDate,
                Premium = msg.PremiumChange,
                FeeAmount = msg.FeeAmount,
                CommissionRate = msg.CommissionRate,
                CommissionAmount = CommissionCalculator.Calculate(msg.PremiumChange, msg.CommissionRate),
            };
            db.Transactions.Add(row);
            await db.SaveChangesAsync();

            log.LogInformation("Accepted {Type} seq {Sequence} for {PolicyNumber}", msg.Type, msg.Sequence, msg.PolicyNumber);
            return Results.Created($"/api/billing/accounts/{msg.PolicyNumber}", row);
        })
        .WithTags("Inbound")
        .WithSummary("Receive a policy transaction from the policy system");

        var g = app.MapGroup("/api/billing").WithTags("Billing");

        g.MapGet("/accounts/{policyNumber}", async (string policyNumber, BillingDbContext db) =>
        {
            var rows = await db.Transactions.Where(t => t.PolicyNumber == policyNumber)
                .OrderBy(t => t.Sequence).ToListAsync();
            if (rows.Count == 0) return Results.NotFound();

            return Results.Ok(new AccountSummary(policyNumber,
                rows.Sum(r => r.Premium), rows.Sum(r => r.FeeAmount), rows.Sum(r => r.CommissionAmount),
                rows.Count, rows));
        }).WithSummary("Billing account for one policy, with totals");

        g.MapGet("/transactions", async (BillingDbContext db, int take = 50) =>
            Results.Ok(await db.Transactions.OrderByDescending(t => t.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync()))
            .WithSummary("Most recent billing transactions");
    }
}
