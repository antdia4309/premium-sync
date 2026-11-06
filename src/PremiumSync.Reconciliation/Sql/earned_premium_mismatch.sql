-- EarnedPremiumMismatch
-- Independent check: recalculate what each policy's written premium SHOULD be from the original annual amounts,
-- without using the prorated results either system stored. Catches errors where both systems agree and both are wrong.
--
-- For every non-cancellation transaction:
--   annual amount x (days from its effective date to the end date) / days in the term
-- where the end date is the cancellation date if the policy was cancelled, otherwise the expiration date.
-- Days before a transaction's effective date count as zero.
--
-- Each stored transaction is rounded to cents, so allow up to one cent of difference per transaction.
-- Policies with transactions created before AnnualPremiumChange existed are skipped.
WITH policy_terms AS (
    SELECT p.Id,
           p.PolicyNumber,
           COALESCE(
               (SELECT MIN(c.EffectiveDate) FROM pol.Transactions c WHERE c.PolicyId = p.Id AND c.Type = 'Cancellation'),
               p.ExpirationDate)                                        AS EndDate,
           julianday(p.ExpirationDate) - julianday(p.EffectiveDate)     AS TermDays
    FROM pol.Policies p
),
per_policy AS (
    SELECT pt.PolicyNumber,
           SUM(CASE WHEN t.Type = 'Cancellation' THEN 0
                    ELSE CAST(t.AnnualPremiumChange AS REAL)
                         * MAX(0, julianday(pt.EndDate) - julianday(t.EffectiveDate))
                         / pt.TermDays
               END)                                                                         AS ExpectedWritten,
           SUM(CAST(ROUND(CAST(t.PremiumChange AS REAL) * 100) AS INTEGER))                AS WrittenCents,
           COUNT(*)                                                                         AS TransactionCount,
           SUM(CASE WHEN t.Type <> 'Cancellation' AND t.AnnualPremiumChange IS NULL THEN 1 ELSE 0 END) AS MissingInputs
    FROM policy_terms pt
    JOIN pol.Transactions t ON t.PolicyId = pt.Id
    GROUP BY pt.PolicyNumber
)
SELECT PolicyNumber,
       CAST(ROUND(ExpectedWritten * 100) AS INTEGER) AS ExpectedCents,
       WrittenCents,
       TransactionCount
FROM per_policy
WHERE MissingInputs = 0
  AND ABS(CAST(ROUND(ExpectedWritten * 100) AS INTEGER) - WrittenCents) > TransactionCount
ORDER BY PolicyNumber;
