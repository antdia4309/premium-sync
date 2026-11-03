# RCA 002: Transactions stuck when billing is unavailable

> Fill in the bracketed parts with what you saw when you reproduced it, then delete this line.

## Symptom
While the billing service was down, new policy transactions were saved in the policy system but never reached billing. Policy showed [__] transactions; billing showed [__]. They stayed `Failed` even after billing came back.

## How it was caught
Started only the policy service, generated test policies, and checked `GET /api/sync/status`: [__] transactions `Failed`. Started billing and waited. Nothing changed, because nothing ever tried again.

## Root cause
The policy system made exactly one attempt to send each transaction. A failed send was recorded, but there was no mechanism to retry it, so a short outage left a permanent gap between the two systems that someone would have to find and fix by hand.

## Fix
1. **Classify failures.** Temporary problems (billing unreachable, timeouts, 5xx, 429) are retryable. Rejections of the data itself (400 validation, 409 conflict) are not; retrying won't change the answer.
2. **Background retry worker** (`RetryWorker`) checks every few seconds for `Failed` transactions whose retry time has arrived and sends them again, oldest first per policy.
3. **Exponential backoff** (`RetryPolicy`): each failure doubles the wait (e.g. 30s, 60s, 120s), capped, so a struggling billing system isn't hammered.
4. **Error queue.** After the maximum attempts, or on a permanent rejection, a transaction becomes `DeadLettered`. It's visible at `GET /api/sync/errors` and can be resent with `POST /api/sync/errors/{id}/resend` once the cause is fixed.

Retrying is safe because of RCA 001: billing is idempotent, so a "failed" send that actually went through won't be booked twice.

## Verification
- Unit tests: `RetryPolicyTests` (backoff, cap, give-up point, which errors retry) and `TransactionSenderTests` (sent, retry scheduled, dead-lettered on rejection, dead-lettered after max attempts).
- Short outage: [__] transactions failed while billing was down; [__] seconds after billing came back, status showed [__] `Sent`, [__] `Failed`.
- Long outage: a transaction used all [__] attempts and moved to the error queue. After restarting billing, a manual resend returned [__].

## Lessons
- [Your take]

## Known limitation
Retries keep each policy's transactions in order within a batch, but a later transaction can still reach billing before an earlier one that's waiting on a retry. That's the next scenario: out-of-order transactions.
