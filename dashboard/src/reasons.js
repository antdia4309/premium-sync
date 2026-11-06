// Plain-language names for each reason code, in the order an analyst would work them.
export const REASONS = [
  { code: 'StatusMismatch', label: 'Disagree on cancellation', help: 'One system says the policy is cancelled, the other says it’s in force.' },
  { code: 'MissingInBilling', label: 'Never reached billing', help: 'The policy system thinks billing accepted it, but billing has no record.' },
  { code: 'MissingInPolicy', label: 'Billed but not on the policy', help: 'Billing booked something the policy system never sent.' },
  { code: 'StuckInQueue', label: 'Stuck in the queue', help: 'Still undelivered after the in-flight window. Needs someone to resend it.' },
  { code: 'PremiumMismatch', label: 'Premium differs', help: 'Same transaction, different premium on each side.' },
  { code: 'FeeMismatch', label: 'Fee differs', help: 'Same transaction, different fee on each side.' },
  { code: 'CommissionMismatch', label: 'Commission differs', help: 'Commission booked doesn’t match written premium times the rate.' },
  { code: 'EarnedPremiumMismatch', label: 'Both agree, both wrong', help: 'Written premium doesn’t match an independent recalculation from the annual amounts.' },
];

export const REASON_BY_CODE = Object.fromEntries(REASONS.map((r) => [r.code, r]));

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
export const formatMoney = (v) => (v === null || v === undefined ? '' : money.format(v));
export const formatDifference = (v) => (v === null || v === undefined ? '' : `${v > 0 ? '+' : ''}${money.format(v)}`);

export const formatWhen = (iso) =>
  new Date(iso.endsWith('Z') ? iso : `${iso}Z`).toLocaleString('en-US', {
    month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit',
  });
