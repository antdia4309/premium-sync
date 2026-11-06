# Reconciliation queries

Each query runs against both databases at once: the policy database is attached as `pol`, billing as `bil`.

**Money is compared in integer cents.** SQLite stores .NET `decimal` values as text, and comparing them as floating point
can create false one-cent mismatches. Every amount is converted with `CAST(ROUND(CAST(x AS REAL) * 100) AS INTEGER)`,
which is exact because every stored amount has at most two decimal places.

| File | Reason code | Level |
|------|-------------|-------|
| `missing_in_billing.sql` | `MissingInBilling` | transaction |
| `stuck_in_queue.sql` | `StuckInQueue` | transaction |
| `missing_in_policy.sql` | `MissingInPolicy` | transaction |
| `amount_mismatch.sql` | `PremiumMismatch`, `FeeMismatch` | transaction |
| `commission_mismatch.sql` | `CommissionMismatch` | policy |
| `status_mismatch.sql` | `StatusMismatch` | policy |
| `earned_premium_mismatch.sql` | `EarnedPremiumMismatch` | policy (independent recalculation) |
