-- MissingInPolicy
-- Billing booked something the policy system has no record of.
-- Billing should only ever book what the policy system sent, so this always needs investigation.
SELECT b.PolicyNumber,
       b.SourceTransactionId AS TransactionId,
       b.Sequence,
       b.Type,
       CAST(ROUND(CAST(b.Premium AS REAL) * 100) AS INTEGER) AS BillingCents
FROM bil.Transactions b
LEFT JOIN pol.Transactions t ON t.TransactionId = b.SourceTransactionId
WHERE t.Id IS NULL
ORDER BY b.PolicyNumber, b.Sequence;
