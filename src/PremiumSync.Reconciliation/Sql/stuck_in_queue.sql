-- StuckInQueue
-- Not delivered (Pending, Failed, Held, or DeadLettered) and older than the cutoff.
-- Recent undelivered transactions are "in flight" and expected; old ones need a person.
SELECT p.PolicyNumber,
       t.TransactionId,
       t.Sequence,
       t.Type,
       t.SyncStatus,
       t.SyncError,
       t.CreatedAtUtc,
       CAST(ROUND(CAST(t.PremiumChange AS REAL) * 100) AS INTEGER) AS PolicyCents
FROM pol.Transactions t
JOIN pol.Policies p ON p.Id = t.PolicyId
WHERE t.SyncStatus <> 'Sent'
  AND t.CreatedAtUtc < @cutoffUtc
ORDER BY p.PolicyNumber, t.Sequence;
