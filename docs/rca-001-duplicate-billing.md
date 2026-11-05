# RCA 001: Duplicate submission double-books premium and commission

## Symptom
A policy's billing account showed 2 transactions and $360.00 commission for a single new business transaction. Expected: 1 transaction and $180.00 commission.

## How it was caught
Sent the same `PolicyTransactionMessage` (same `TransactionId`) to `/api/inbound/transactions` twice, simulating a sender retry after a timeout. Both calls returned 201 Created, and `GET /api/billing/accounts/PS-DUP-001` showed both rows.

## Root cause
Billing treated every inbound message as new. Nothing checked whether a `TransactionId` had already been booked, and the index on `SourceTransactionId` wasn't unique, so the database accepted the second copy too.

Why it matters in the real world: senders retry. A timeout doesn't mean the first call failed, it means the sender doesn't *know*. Without idempotency, every retry is a potential double charge and double commission.

## Fix
1. **Idempotency check** before booking (`ReplayCheck`):
   - New `TransactionId` → book it (`201 Created`)
   - Same `TransactionId`, same data → it's a retry; return the original row, book nothing (`200 OK`)
   - Same `TransactionId`, different data → the key was reused for something else; reject (`409 Conflict`)
2. **Unique index** on `SourceTransactionId` as a safety net, so even two copies arriving at the exact same moment can't both be saved.

## Verification
- Unit tests in `ReplayCheckTests` cover new, duplicate, and conflict cases.
- Re-ran the repro: first send 201 Created, second send 200 OK, conflicting send 409 Conflict. Account shows 1 row and $180.00 commission.

## Lessons
- Always validate on the receiving side, even when the sender is internal. Without it, duplicate transactions flow into downstream platforms, causing billing errors for the customer and incorrect commission.
