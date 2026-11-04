using Microsoft.EntityFrameworkCore;
using PremiumSync.PolicyService.Data;
using PremiumSync.PolicyService.Sync;

namespace PremiumSync.PolicyService.Endpoints;

public record SyncItemDto(Guid TransactionId, string PolicyNumber, int Sequence, string Type, string SyncStatus,
    int AttemptCount, DateTime? LastAttemptAtUtc, DateTime? NextAttemptAtUtc, string? SyncError);

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sync").WithTags("Sync");

        g.MapGet("/status", async (PolicyDbContext db) =>
        {
            var counts = await db.Transactions.GroupBy(t => t.SyncStatus)
                .Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync();
            return Results.Ok(Enum.GetValues<SyncStatus>().ToDictionary(
                s => s.ToString(), s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0));
        }).WithSummary("How many transactions are in each sync status");

        g.MapGet("/retrying", (PolicyDbContext db) => ListAsync(db, SyncStatus.Failed))
            .WithSummary("Transactions waiting for an automatic retry");

        g.MapGet("/held", (PolicyDbContext db) => ListAsync(db, SyncStatus.Held))
            .WithSummary("Transactions waiting for an earlier transaction on the same policy");

        g.MapGet("/errors", (PolicyDbContext db) => ListAsync(db, SyncStatus.DeadLettered))
            .WithSummary("Error queue: transactions that need a person to look at them");

        g.MapPost("/errors/{transactionId:guid}/resend", async (Guid transactionId, PolicyDbContext db, TransactionSender sender) =>
        {
            var txn = await db.Transactions.Include(t => t.Policy).ThenInclude(p => p!.Transactions)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
            if (txn is null) return Results.NotFound();
            if (txn.SyncStatus != SyncStatus.DeadLettered)
                return Results.Conflict(new { error = $"Only dead-lettered transactions can be resent. This one is {txn.SyncStatus}." });

            txn.AttemptCount = 0;   // fresh start: a person decided it's worth another go
            await sender.SendAsync(txn);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(txn));
        }).WithSummary("Manually resend a dead-lettered transaction (after fixing the cause)");
    }

    private static async Task<IResult> ListAsync(PolicyDbContext db, SyncStatus status)
    {
        var rows = await db.Transactions.Include(t => t.Policy)
            .Where(t => t.SyncStatus == status)
            .OrderBy(t => t.PolicyId).ThenBy(t => t.Sequence)
            .Take(200).ToListAsync();
        return Results.Ok(rows.Select(ToDto));
    }

    private static SyncItemDto ToDto(PolicyTransaction t) => new(t.TransactionId, t.Policy!.PolicyNumber, t.Sequence,
        t.Type.ToString(), t.SyncStatus.ToString(), t.AttemptCount, t.LastAttemptAtUtc, t.NextAttemptAtUtc, t.SyncError);
}
