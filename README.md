# Premium Sync

A policy-to-billing integration simulator built with .NET 10 minimal APIs and SQLite.

A mock **policy system** binds, endorses, and cancels policies. Every premium-bearing transaction is sent to a mock **billing system**, which validates it against the integration contract and calculates commission. The project is designed to break in the ways real insurance integrations break, and to show how to catch, explain, and fix each failure.

```
[Policy Service :5001] --PolicyTransactionMessage--> [Billing Service :5002]
  bind, endorse, cancel                               validate, book premium,
  prorate premium                                     calculate commission
  track sync status                                   ledger per policy
```

## Why I built it

I've spent 10 years in P&C insurance, most recently as a business analyst tracing premium, fee, and commission discrepancies across integrated policy, billing, and finance systems. This project rebuilds that problem space from the code side. All data is synthetic and every scenario is generic.

## What's in Phase 1

- **Policy Service**: new business, pro-rata endorsements, pro-rata cancellations. Each transaction is saved first, then sent to billing, with its sync status (`Sent` / `Failed`) recorded.
- **Billing Service**: one inbound endpoint that enforces the contract (sequence rules, sign rules, whole cents, commission rate range) and rejects bad messages with a 400 and field-level errors.
- **Shared contract** (`PremiumSync.Contracts`): the message both sides agree on.
- **Unit tests** for proration, commission rounding, and contract validation.
- **Seed endpoint** that generates realistic test policies.

## Run it

Open in GitHub Codespaces (the dev container installs .NET 10), then:

```bash
./run.sh
```

- Policy API docs: http://localhost:5001/scalar
- Billing API docs: http://localhost:5002/scalar

Then generate data:

```bash
curl -X POST "http://localhost:5001/api/dev/seed?count=100"
```

Or open `requests.http` and click **Send Request** on each example.

Run the tests:

```bash
dotnet test
```

## Roadmap

- [x] **Phase 1, happy path:** policy and billing services, contract validation, proration, commission, tests
- [ ] **Phase 2, realistic failures:** duplicate submissions and idempotency, out-of-order transactions, retries and an error queue for when billing is down, rounding drift, multi-period edge cases, $0 cancellations
- [ ] **Phase 3, reconciliation:** SQL report comparing policy vs. billing with mismatch reason codes, a small React dashboard, CI on every push
- [ ] **Phase 4, write-ups:** RCA docs for each failure scenario in `/docs`

## Project layout

```
src/
  PremiumSync.Contracts/       shared message contract
  PremiumSync.PolicyService/   policy system (port 5001)
  PremiumSync.BillingService/  billing system (port 5002)
tests/
  PremiumSync.Tests/           xUnit tests
docs/                          RCA write-ups (Phase 2+)
```

## Design notes

- **Save, then send.** The policy system records every transaction before calling billing, so an outage never loses data. Failed sends stay visible for retry.
- **Billing doesn't trust the sender.** All contract rules are enforced on the receiving side.
- **Money is `decimal`, rounded to cents, half away from zero**, and that rule is covered by tests on both positive and negative amounts.
- **Known simplification:** cancellation return premium applies the remaining-term fraction to total written premium. That's exact without endorsements; making it exact for endorsed policies is a Phase 2 task.
