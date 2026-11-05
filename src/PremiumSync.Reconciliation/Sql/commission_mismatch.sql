-- CommissionMismatch
-- Policy-level check: commission billing booked vs. commission the policy's written premium should earn.
-- Only fully synced policies are compared; an in-flight transaction would make every total look wrong.
--
-- Expected commission is computed in whole numbers to avoid floating-point rounding:
--   written premium in cents x commission rate in millionths, then rounded half away from zero to cents.
WITH policy_side AS (
    SELECT p.PolicyNumber,
           CAST(ROUND(CAST(p.CommissionRate AS REAL) * 1000000) AS INTEGER) AS RateMicros,
           SUM(CAST(ROUND(CAST(t.PremiumChange AS REAL) * 100) AS INTEGER)) AS WrittenCents,
           SUM(CASE WHEN t.SyncStatus <> 'Sent' THEN 1 ELSE 0 END)           AS UnsentCount
    FROM pol.Policies p
    JOIN pol.Transactions t ON t.PolicyId = p.Id
    GROUP BY p.PolicyNumber, p.CommissionRate
),
expected AS (
    SELECT PolicyNumber,
           WrittenCents,
           UnsentCount,
           WrittenCents * RateMicros AS Product,
           CASE WHEN WrittenCents * RateMicros >= 0
                THEN  (WrittenCents * RateMicros + 500000) / 1000000
                ELSE -((-(WrittenCents * RateMicros) + 500000) / 1000000)
           END AS ExpectedCommissionCents
    FROM policy_side
),
billing_side AS (
    SELECT PolicyNumber,
           SUM(CAST(ROUND(CAST(CommissionAmount AS REAL) * 100) AS INTEGER)) AS BookedCommissionCents
    FROM bil.Transactions
    GROUP BY PolicyNumber
)
SELECT e.PolicyNumber,
       e.ExpectedCommissionCents,
       b.BookedCommissionCents
FROM expected e
JOIN billing_side b ON b.PolicyNumber = e.PolicyNumber
WHERE e.UnsentCount = 0
  AND e.ExpectedCommissionCents <> b.BookedCommissionCents
ORDER BY e.PolicyNumber;
