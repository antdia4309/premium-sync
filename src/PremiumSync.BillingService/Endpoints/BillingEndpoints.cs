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

            // Idempotency check: have we already booked this TransactionId?
            var existing = await db.Transactions.FirstOrDefaultAsync(t => t.SourceTransactionId == msg.TransactionId);
            switch (ReplayCheck.Evaluate(existing, msg))
            {
                case ReplayResult.Duplicate:
                    log.LogInformation("Duplicate {TransactionId} for {PolicyNumber} ignored", msg.TransactionId, msg.PolicyNumber);
                    return Results.Ok(existing);   // same answer as the first time, nothing new booked
                case ReplayResult.Conflict:
                    log.LogWarning("Conflict: {TransactionId} reused with different data", msg.TransactionId);
                    return Results.Conflict(new { error = "TransactionId was already used for a different transaction.", existing });
            }

            // Everything already booked for this policy: used for the ordering check and cumulative commission.
            var prior = await db.Transactions.Where(t => t.PolicyNumber == msg.PolicyNumber).ToListAsync();
            int? lastBooked = prior.Count == 0 ? null : prior.Max(t => t.Sequence);

            // Ordering check: is this the next sequence we expect for this policy?
            switch (SequenceCheck.Evaluate(lastBooked, msg.Sequence))
            {
                case SequenceResult.TooEarly:
                    log.LogWarning("Out of order: {PolicyNumber} seq {Sequence} arrived, expected {Expected}",
                        msg.PolicyNumber, msg.Sequence, SequenceCheck.Expected(lastBooked));
                    return Results.Problem(statusCode: ContractStatusCodes.OutOfSequence, title: "Out of sequence",
                        detail: $"Expected sequence {SequenceCheck.Expected(lastBooked)} for {msg.PolicyNumber}, received {msg.Sequence}.");
                case SequenceResult.AlreadyUsed:
                    return Results.Conflict(new { error = $"Sequence {msg.Sequence} is already booked for {msg.PolicyNumber} under a different TransactionId." });
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
                CommissionAmount = CommissionFor(prior, msg),
            };
            db.Transactions.Add(row);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two copies arrived at the same moment and both passed the check above.
                // The unique index stopped the second one; answer with the row that won.
                var winner = await db.Transactions.AsNoTracking().FirstAsync(t => t.SourceTransactionId == msg.TransactionId);
                return Results.Ok(winner);
            }

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

    /// <summary>
    /// Cumulative commission keeps the policy's commission total exact (no penny drift).
    /// It assumes one commission rate per policy; if the rate ever changes mid-policy,
    /// fall back to per-transaction commission rather than spreading the new rate over old premium.
    /// </summary>
    private static decimal CommissionFor(List<BillingTransaction> prior, PolicyTransactionMessage msg)
    {
        if (prior.Any(t => t.CommissionRate != msg.CommissionRate))
            return CommissionCalculator.Calculate(msg.PremiumChange, msg.CommissionRate);

        return CommissionCalculator.Cumulative(
            prior.Sum(t => t.Premium), prior.Sum(t => t.CommissionAmount), msg.PremiumChange, msg.CommissionRate);
    }
}
