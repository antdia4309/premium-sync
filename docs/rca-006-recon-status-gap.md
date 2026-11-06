# RCA 006: Reconciliation missed a cancelled status with no cancellation

## Symptom
A policy marked `Cancelled` in the policy system, with no cancellation transaction anywhere and no cancellation in billing, produced **no** `StatusMismatch` finding. The two systems disagreed about whether the policy existed, and the report said everything was fine.

## How it was caught
While designing the `StatusMismatch` injection for Phase 3, Part 2. The simplest way to corrupt a policy's status is to flip it to `Cancelled` without creating a cancellation transaction, which is also the end state the RCA 005 bug produced. Walking through the query with that data showed it would be skipped.

## Root cause
`status_mismatch.sql` only flagged a cancelled policy if its cancellation transaction was older than the "in flight" cutoff:

```sql
WHERE p.Status = 'Cancelled' AND bp.BillingCancelled = 0 AND pc.CancelledAtUtc < @cutoffUtc
```

With no cancellation transaction, `pc.CancelledAtUtc` is `NULL`. In SQL, `NULL < anything` is neither true nor false; it's `NULL`, which `WHERE` treats as false. So the exact case the check existed for was silently filtered out.

## Fix
Treat a missing cancellation as "not in flight":

```sql
WHERE p.Status = 'Cancelled' AND bp.BillingCancelled = 0
  AND (pc.CancelledAtUtc IS NULL OR pc.CancelledAtUtc < @cutoffUtc)
```

## Verification
- New test `Cancelled_status_with_no_cancellation_transaction_is_a_status_mismatch`.
- The `StatusMismatch` injection creates exactly this state, and `DiscrepancyInjectorTests` asserts it's reported.

## Lessons
- A reconciliation report is code, and it can have bugs too. A clean report only means something if you've proven it can find problems, which is why every reason code now has an injection and a test that the report catches it.
- Any comparison against a column from a `LEFT JOIN` can be `NULL`. Decide explicitly what a missing row should mean instead of letting `NULL` decide.
