-- MissingInBilling
-- The policy system says billing accepted this transaction (SyncStatus = 'Sent'),
-- but billing has no row for it. Nothing will ever retry it, because the policy side thinks it's done.
SELECT p.PolicyNumber,
       t.TransactionId,
       t.Sequence,
       t.Type,
       CAST(ROUND(CAST(t.PremiumChange AS REAL) * 100) AS INTEGER) AS PolicyCents
FROM pol.Transactions t
JOIN pol.Policies p ON p.Id = t.PolicyId
LEFT JOIN bil.Transactions b ON b.SourceTransactionId = t.TransactionId
WHERE b.Id IS NULL
  AND t.SyncStatus = 'Sent'
ORDER BY p.PolicyNumber, t.Sequence;
