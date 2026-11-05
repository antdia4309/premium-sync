# RCA 003: Transactions reach billing out of order

## Symptom
A cancellation was booked in billing before the endorsement it followed. Right after the cancellation, billing's account for the policy showed sequences 1 and 3 and total premium $821.71, while the policy system showed written premium $1,122.53. When the endorsement was finally delivered, billing's booking order was 1, 3, 2 instead of 1, 2, 3.

## How it was caught
Bound a policy, stopped billing, and endorsed it. To make the repro deterministic, retries were set to a single attempt, so the endorsement went straight to the error queue instead of racing a retry. Restarted billing and cancelled the policy. The cancellation sent immediately and was booked, even though the endorsement before it hadn't been delivered. Resending the endorsement from the error queue then booked it after the cancellation.

## Root cause
Retries made each transaction's delivery independent. A later transaction could go out the moment it was created, even though an earlier transaction on the same policy was still waiting to retry or sitting in the error queue. Billing booked whatever arrived, in whatever order it arrived.

Why it matters: a cancellation's return premium is calculated from everything before it. If billing books it without the endorsement, billing's totals are wrong until the endorsement catches up, and anything reading billing in that window (invoices, commission statements, downstream feeds) picks up bad numbers. If the endorsement is stuck in the error queue, the window never closes on its own.

## Fix
Two layers, sender and receiver:
1. **Sender holds** (`TransactionSender`): before sending, the policy system checks for any earlier transaction on the same policy that isn't `Sent`. If there is one, the new transaction becomes `Held` instead of sending. Holding doesn't count as an attempt. The retry worker re-checks held transactions every tick and releases them, in sequence order, as soon as the blocker is delivered.
2. **Receiver enforces** (`SequenceCheck`): billing only books the next expected sequence for each policy.
   - Next in line → booked
   - Skips ahead → `425 Too Early` with the expected sequence. The sender treats this as retryable, since the earlier one may still arrive.
   - Sequence already booked under a different `TransactionId` → `409 Conflict`

Billing's check runs after the idempotency check (RCA 001), so a retry of something already booked still gets its original answer instead of an ordering error.

## Verification
- Unit tests: `SequenceCheckTests` (in order, too early, already used) and new `TransactionSenderTests` cases (held behind Failed, DeadLettered, and Held predecessors; sends once the earlier one is Sent; 425 retries).
- Re-ran the repro with the fix: right after the cancel, the cancellation showed `Held` with the message "Waiting for sequence 2 (DeadLettered) to reach billing first," and billing had only sequence 1 ($1,200.00). After resending the endorsement from the error queue, within a few seconds status showed 3 `Sent` and 0 `Held`, and billing's booking order was 1, 2, 3. Billing total $1,122.53 matched policy written premium $1,122.53.
- Sent sequence 3 straight to billing for a new policy: `425 Too Early` ("Expected sequence 1").

## Lessons
- An out-of-order transaction doesn’t just cause a brief mismatch. If the earlier one gets stuck, the later one sits in billing with wrong totals indefinitely. Ordering has to be enforced, not assumed. 

- Fixing one integration problem can create another. Retries solved stuck transactions but made delivery order unpredictable. Each fix needs to be checked against the ones before it.

## Known limitation
A transaction held behind one in the error queue stays held until someone resends the blocker. That's intentional (sending it would recreate the bug), and it's visible at `GET /api/sync/held` with the reason.

> Note: premium figures in this RCA reflect the cancellation math before [RCA 004](rca-004-rounding-drift.md) corrected it.
