-- StatusMismatch
-- Policy-level check: does billing agree on whether the policy is cancelled?
--   - Policy says Cancelled, billing has no cancellation, and either there's no cancellation transaction at all
--     or it's older than the cutoff (so it's not just in flight)
--   - Billing has a cancellation, policy says InForce
WITH billing_policies AS (
    SELECT PolicyNumber,
           MAX(CASE WHEN Type = 'Cancellation' THEN 1 ELSE 0 END) AS BillingCancelled
    FROM bil.Transactions
    GROUP BY PolicyNumber
),
policy_cancellations AS (
    SELECT PolicyId, MIN(CreatedAtUtc) AS CancelledAtUtc
    FROM pol.Transactions
    WHERE Type = 'Cancellation'
    GROUP BY PolicyId
)
SELECT p.PolicyNumber,
       p.Status AS PolicyStatus,
       CASE WHEN bp.BillingCancelled = 1 THEN 'Cancelled' ELSE 'InForce' END AS BillingStatus
FROM pol.Policies p
JOIN billing_policies bp ON bp.PolicyNumber = p.PolicyNumber
LEFT JOIN policy_cancellations pc ON pc.PolicyId = p.Id
WHERE (p.Status = 'Cancelled' AND bp.BillingCancelled = 0 AND (pc.CancelledAtUtc IS NULL OR pc.CancelledAtUtc < @cutoffUtc))
   OR (p.Status <> 'Cancelled' AND bp.BillingCancelled = 1)
ORDER BY p.PolicyNumber;
