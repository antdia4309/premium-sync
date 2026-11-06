# Premium Sync

A policy-to-billing integration simulator built with .NET 10 minimal APIs and SQLite.

A mock **policy system** binds, endorses, and cancels policies. Every premium-bearing transaction is sent to a mock **billing system**, which validates it against the integration contract and calculates commission. The project is designed to break in the ways real insurance integrations break, and to show how to catch, explain, and fix each failure.

```
[Policy Service :5001] --PolicyTransactionMessage--> [Billing Service :5002]
  bind, endorse, cancel                               validate, book premium,
  prorate premium                                     calculate commission
  retries, ordering, error queue                      ledger per policy
          \                                                 /
           '--------> [Reconciliation :5003] <-------------'
                       SQL across both databases:
                       what doesn't match, and why
```

![Reconciliation dashboard: 9 discrepancies across 8 policies, grouped by reason, with a findings list](docs/images/dashboard.png)

## Why I built it

I've spent 10 years in P&C insurance, most recently as a business analyst tracing premium, fee, and commission discrepancies across integrated policy, billing, and finance systems. This project rebuilds that problem space from the code side. All data is synthetic and every scenario is generic.

## What it does

- **Policy Service:** new business, pro-rata endorsements, and pro-rata cancellations. Every transaction is saved first, then sent to billing, and its delivery status is tracked (`Sent`, `Failed`, `Held`, `DeadLettered`).
- **Billing Service:** one inbound endpoint that enforces the integration contract (sequence order, sign rules, whole cents, commission rate range, no negative balances), books premium, and calculates commission.
- **Shared contract** (`PremiumSync.Contracts`): the message and status codes both sides agree on.
- **Failure handling:** idempotent billing, background retries with exponential backoff, an error queue with manual resend, and per-policy ordering.
- **Sync endpoints** (`/api/sync/...`): delivery status counts, retrying, held, and error-queue views.
- **105 tests** covering proration, cancellation math, commission, contract validation, retries, ordering, business rules, the reconciliation SQL against real databases, and that every injected discrepancy is caught. CI runs them on every push.
- **Reconciliation Service:** compares the two databases with SQL and reports every mismatch with a reason code. Each run is saved with its findings.
- **React dashboard:** a one-line verdict, counts by reason, run history, and side-by-side ledgers for any policy.
- **Dev endpoints** that generate realistic test policies, demonstrate commission drift, and inject every kind of discrepancy for reconciliation to find.

## Run it

Open in GitHub Codespaces (the dev container installs .NET 10), then:

```bash
./run.sh
```

- Dashboard: http://localhost:5173
- Policy API docs: http://localhost:5001/scalar
- Billing API docs: http://localhost:5002/scalar
- Reconciliation API docs: http://localhost:5003/scalar

From the dashboard: **Add 100 test policies**, then **Run reconciliation** (clean), then **Inject problems** and **Run reconciliation** again (every reason code shows up).

Then generate data:

```bash
curl -X POST "http://localhost:5001/api/dev/seed?count=100"
```

Or open `requests.http` and click **Send Request** on each example.

Run the tests:

```bash
dotnet test
```

## Dashboard

A React page (Vite, port 5173) for working through a reconciliation the way an ops analyst would:

- **A one-line verdict** for the selected run: either the systems agree, or how many discrepancies across how many policies
- **Counts by reason**, in plain language; click one to filter the findings
- **Run history**, to compare today with earlier runs
- **Side-by-side ledgers** for any policy: the policy system in blue, billing in green, and every disagreement in red

![Side-by-side ledger for one policy, with a transaction billing booked that the policy system never sent](docs/images/ledger.png)

The dashboard calls all three services through its dev server's proxy, so the APIs need no CORS setup.

## Reconciliation

`POST /api/recon/runs` attaches both SQLite databases to one connection and runs the queries in [`src/PremiumSync.Reconciliation/Sql`](src/PremiumSync.Reconciliation/Sql). Every finding gets a reason code:

| Reason code | Level | Meaning |
|-------------|-------|---------|
| `MissingInBilling` | Transaction | Policy system marked it Sent; billing has no record of it |
| `StuckInQueue` | Transaction | Undelivered (Failed, Held, DeadLettered) longer than the cutoff |
| `MissingInPolicy` | Transaction | Billing booked something the policy system doesn't have |
| `PremiumMismatch` / `FeeMismatch` | Transaction | Same transaction, different amount |
| `CommissionMismatch` | Policy | Booked commission ≠ written premium × rate (fully synced policies only) |
| `StatusMismatch` | Policy | The systems disagree on whether the policy is cancelled |
| `EarnedPremiumMismatch` | Policy | Written premium doesn't match an **independent recalculation** from the original annual amounts. Catches errors where both systems agree and both are wrong |

Money is compared in integer cents, and expected commission is calculated with integer math, so SQL rounds exactly the way the C# code does.

**Proving the report works:** in Development, `POST /api/dev/inject/all` corrupts the databases directly (bypassing every rule in both services) with one discrepancy of each kind, each on a different healthy policy, and says which findings to expect. Tests do the same for every kind, so a clean report means "nothing is wrong," not "the check is broken."

## Roadmap

- [x] **Phase 1, happy path:** policy and billing services, contract validation, proration, commission, tests
- [x] **Phase 2, realistic failures**
  - [x] Duplicate submissions → idempotency check + unique index ([RCA 001](docs/rca-001-duplicate-billing.md))
  - [x] Billing outages → automatic retries with backoff and an error queue ([RCA 002](docs/rca-002-billing-outage.md))
  - [x] Out-of-order transactions → sender holds behind earlier sequences, billing enforces order ([RCA 003](docs/rca-003-out-of-order.md))
  - [x] Rounding drift → exact per-transaction cancellation return and cumulative commission ([RCA 004](docs/rca-004-rounding-drift.md))
  - [x] Negative premium and $0 boundary transactions → change-date rules, premium floor on both sides ([RCA 005](docs/rca-005-zero-and-boundary.md))
- [ ] **Phase 3, reconciliation** (in progress)
  - [x] Reconciliation service: SQL comparison with reason codes, saved run history, EF migrations for all databases
  - [x] Injected discrepancies and an independent earned-premium check ([RCA 006](docs/rca-006-recon-status-gap.md))
  - [x] React dashboard: verdict, counts by reason, run history, side-by-side ledgers
- [x] **Write-ups:** a root cause analysis for every failure scenario ([docs](docs/README.md))

### Backlog
- Multi-period (multi-year) policies: period numbers in the contract, per-period proration and reconciliation

## Project layout

```
src/
  PremiumSync.Contracts/       shared message contract
  PremiumSync.PolicyService/   policy system (port 5001)
  PremiumSync.BillingService/  billing system (port 5002)
  PremiumSync.Reconciliation/  reconciliation (port 5003); SQL lives in Sql/
dashboard/                     React dashboard (port 5173)
tests/
  PremiumSync.Tests/           xUnit tests
docs/                          RCA write-ups (Phase 2+)
```

## Design notes

- **Save, then send.** The policy system records every transaction before calling billing, so an outage never loses data. Failed sends stay visible for retry.
- **Billing doesn't trust the sender.** All contract rules are enforced on the receiving side.
- **Failures are classified, retried, then parked.** Temporary failures (billing down, timeouts, 5xx) retry automatically in the background with exponential backoff. Permanent rejections (400, 409) and transactions that run out of attempts go to an error queue for a person to review and resend.
- **Money is `decimal`, rounded to cents, half away from zero**, and that rule is covered by tests on both positive and negative amounts.
- **Cancellation returns each transaction's own unearned share** (based on the days that transaction covered), added unrounded and rounded once.
- **Commission is cumulative:** each transaction's commission is the commission on all premium to date minus what's already booked, so per-transaction rounding can't drift the total.
- **Order is guaranteed per policy.** The policy system won't send a transaction while an earlier one on the same policy is undelivered (it's `Held` instead), and billing only books the next expected sequence, answering anything that skips ahead with `425 Too Early`.
- **Bad changes are rejected before anything changes.** Endorsements and cancellations must take effect before expiration, and written premium can't go below $0.00. Billing enforces the same floor on its side. A rejected request leaves both systems untouched.
- **Schema changes use EF Core migrations.** Each service applies pending migrations on startup.
- **Billing is idempotent.** Every transaction carries a `TransactionId`. A retry with the same ID and data returns the original result without booking anything new; the same ID with different data is rejected with `409 Conflict`. A unique index backs this up at the database level.
