# Docs

Root cause analyses for each failure scenario in the project.

| # | Scenario | Fix |
|---|----------|-----|
| [001](rca-001-duplicate-billing.md) | Duplicate submission double-books premium and commission | Idempotency check + unique index |
| [002](rca-002-billing-outage.md) | Transactions stuck when billing is unavailable | Retry worker with backoff + error queue |
| [003](rca-003-out-of-order.md) | Transactions reach billing out of order | Sender holds + billing sequence check |
| [004](rca-004-rounding-drift.md) | Cancellation return and commission drift | Per-transaction unearned share + cumulative commission |
| [005](rca-005-zero-and-boundary.md) | Negative premium and $0 boundary transactions | Change-date rules + premium floor on both sides |
| [006](rca-006-recon-status-gap.md) | Reconciliation missed a cancelled status with no cancellation | Explicit `NULL` handling in the status query |

## RCA template

1. **Symptom**: what someone would report ("billing shows double commission on PS-000123")
2. **How it was caught**: the test, log, or reconciliation query that surfaced it
3. **Root cause**: what actually broke and why
4. **Fix**: the code or design change, and the test that now guards it
5. **Verification**: how you proved the fix works
6. **Lessons**: what you'd do differently or watch for next time