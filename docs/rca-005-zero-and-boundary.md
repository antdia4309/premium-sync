# RCA 005: Negative premium and $0 boundary transactions


## Symptom
Probing the edges of the policy rules turned up three bugs, one of which left the two systems permanently out of sync:

1. **Negative written premium.** An annual −$2,000 endorsement on a $1,200 policy was accepted. Written premium became −$800.00, and billing booked −$300.00 commission against $180.00 originally paid.
2. **Cancelling that policy broke the integration.** The cancellation calculated a return of **+$469.04**, a *charge* to the insured instead of a refund. Billing correctly rejected it (a cancellation can't be positive), so it went to the error queue. But the policy system had already marked the policy **Cancelled**. Policy said cancelled; billing said in force. No retry could fix it, because billing's answer would never change.
3. **$0 transactions on the expiration date.** A cancellation or endorsement effective on the expiration date was accepted and booked as a $0 transaction. The cancellation also flagged a fully earned policy as Cancelled, which downstream processes (cancellation notices, reporting) would treat as a real cancellation.

Two $0 cases turned out to be legitimate and were kept: a $0 endorsement that only adds a fee (e.g., a name change), and cancelling a policy whose premium was already reduced to $0.

## How it was caught
Deliberately probed boundary inputs (a reduction larger than the premium, changes dated on the expiration date, $0 changes) instead of only realistic ones, and compared each result with what billing booked.

## Root cause
The policy system checked that dates fell *within* the term (inclusive of the expiration date) and never checked what an endorsement would do to the policy's total. Billing validated each transaction on its own, so it didn't see that the account was going negative either. The one rule billing *did* enforce, "a cancellation can't be positive", existed only on the billing side, so the policy system created a transaction billing was guaranteed to reject, after already changing the policy's status.

## Fix
1. **Change dates** (`PolicyRules.CheckChangeDate`): endorsements and cancellations must take effect on or after the effective date and **before** expiration. On the expiration date the policy is fully earned, so there's nothing to change.
2. **Premium floor** (`PolicyRules.CheckWrittenPremium`): an endorsement can't take written premium below $0.00. Reducing to exactly $0.00 is allowed.
3. **Billing account floor** (`TransactionValidator.ValidateAgainstAccount`): billing rejects any transaction that would bring the account's total premium below 0.00, even if the sender allowed it.

All three are checked **before** anything is created or any status changes, so a rejected request leaves both systems untouched.

## Verification
- Unit tests in `PolicyRulesTests`: change-date boundaries (effective date allowed, day before expiration allowed, expiration date rejected, before effective rejected), premium floor (below zero rejected, exactly zero allowed, zero change allowed), billing account floor, and $0 cancellation booking $0 commission.
- Re-ran every probe: the −$2,000 endorsement returns `400` with "would reduce written premium to -$800.00"; cancel and endorse on the expiration date return `400`; the $0 fee-only endorsement and the $0 cancellation still succeed; a direct −$2,000 to billing returns `400`. Sync status shows nothing in the error queue.

## Lessons
- Validate before you change anything. The worst bug here wasn’t the negative premium, it was the policy changing its status before finding out billing would reject the transaction. Once that happens, no retry can fix it.

## Out of scope
Multi-period (multi-year) policies were originally planned for this part. The system can't represent them yet: every policy is a single one-year term. Supporting them means adding period numbers to the integration contract and reworking proration per period, so it's tracked in the README backlog as a feature rather than a bug fix.
