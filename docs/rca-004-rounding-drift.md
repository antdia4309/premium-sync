# RCA 004: Cancellation return premium and commission drift

> Confirm the numbers below match what you saw, add your lesson, then delete this line.

Two money bugs with the same theme: a calculation that's right for the simple case and quietly wrong once a policy has more than one transaction.

## Symptom
**Cancellation:** A $1,200 policy (bound 1/1) with a +$600 annual endorsement (effective 7/2, prorated to $300.82) was cancelled effective 10/1. The cancellation returned $378.29, leaving written premium at $1,122.53. Hand calculation of earned premium: $1,200 × 273/365 + $600 × 91/365 = **$1,047.12**. The policy was over-charged by $75.41.

**Commission:** A $1,000 policy at 12.5% commission with twenty +$1.00 endorsements. Billing booked $127.60 in commission; 12.5% of $1,020 is **$127.50**. Billing over-paid commission by $0.10.

## How it was caught
- Cancellation: compared the policy's written premium after the cancel to a hand calculation of earned premium.
- Commission: bound the policy and ran 20 endorsements, then compared billing's commission total to the commission on billing's own premium total.

## Root cause
**Cancellation:** the return was calculated as `written premium × (days remaining ÷ full term)`. That treats every dollar as if it covered the full year. The endorsement's $300.82 was already prorated to cover 7/2 through 1/1 (183 days), so applying 92/365 to it again returned far too little. Only unendorsed policies came out right.

**Commission:** billing rounded commission separately on every transaction. $1.00 × 12.5% is exactly 12.5 cents, a half cent, which rounds up to 13 cents every time. Twenty of them added 10 cents that the total premium never earned. Any rate and amounts that produce fractions of a cent drift the same way, just less predictably.

## Fix
1. **Exact cancellation return** (`PremiumMath.CancellationReturn`): each transaction returns its own unearned share, based on the days *it* covered:
   - cancel on or before its effective date → return all of it
   - otherwise → premium × (days left after cancel ÷ days it covered)

   Shares are added unrounded and rounded once at the end.
2. **Cumulative commission** (`CommissionCalculator.Cumulative`): each new transaction's commission = commission on *all* premium to date − commission already booked. Individual amounts can alternate (13¢, 12¢, 13¢...), but the running total is always exact.
3. Policy responses now include `expectedCommission` (written premium × rate), so commission can be compared across systems. That's groundwork for Phase 3 reconciliation.

## Verification
- Unit tests: `PremiumMathTests` (unendorsed, endorsed, flat cancel, cancel before an endorsement takes effect, cancel on expiration, earlier premium reduction) and `CumulativeCommissionTests` (drift reproduced as a test, no drift with the fix, alternating amounts, full cancel returns exactly what was booked).
- Re-ran the cancellation scenario: return $453.70, written premium $1,047.12, matching the hand calculation, with billing's premium and commission matching the policy's.
- `POST /api/dev/drift-demo?endorsements=20`: policy expects $127.50, billing booked $127.50, drift $0.00.

## Lessons
- Both systems agreed with each other, and both were wrong. Policy and billing matched on the bad cancellation because they shared the same flawed math. Checking against an independent hand calculation of earned premium is what exposed it. Matching systems doesn’t mean correct systems.

- A tenth of a cent seems too small to matter until it repeats across every transaction on every policy. Calculating the total first and booking the difference (instead of rounding each piece separately) keeps the numbers exact no matter how many transactions a policy has.

## Known limitation
Cumulative commission assumes one commission rate per policy. If the rate changes mid-policy, billing falls back to per-transaction commission for that policy, so small drift is possible there. A rate change would need its own rule (e.g., re-rating only new premium).

> Note: premium figures in this RCA reflect the cancellation math before [RCA 004](rca-004-rounding-drift.md) corrected it.