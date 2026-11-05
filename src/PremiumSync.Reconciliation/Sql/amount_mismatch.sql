-- PremiumMismatch / FeeMismatch
-- Same transaction on both sides (matched on TransactionId), different amounts.
WITH matched AS (
    SELECT p.PolicyNumber,
           t.TransactionId,
           t.Sequence,
           t.Type,
           CAST(ROUND(CAST(t.PremiumChange AS REAL) * 100) AS INTEGER) AS PolicyPremiumCents,
           CAST(ROUND(CAST(b.Premium       AS REAL) * 100) AS INTEGER) AS BillingPremiumCents,
           CAST(ROUND(CAST(t.FeeAmount     AS REAL) * 100) AS INTEGER) AS PolicyFeeCents,
           CAST(ROUND(CAST(b.FeeAmount     AS REAL) * 100) AS INTEGER) AS BillingFeeCents
    FROM pol.Transactions t
    JOIN pol.Policies p ON p.Id = t.PolicyId
    JOIN bil.Transactions b ON b.SourceTransactionId = t.TransactionId
)
SELECT *
FROM matched
WHERE PolicyPremiumCents <> BillingPremiumCents
   OR PolicyFeeCents     <> BillingFeeCents
ORDER BY PolicyNumber, Sequence;
