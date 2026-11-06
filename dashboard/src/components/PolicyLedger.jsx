import { useEffect, useRef, useState } from 'react';
import { api } from '../api.js';
import { REASON_BY_CODE, formatMoney } from '../reasons.js';

const round2 = (n) => Math.round(n * 100) / 100;
const same = (a, b) => a !== undefined && b !== undefined && round2(a) === round2(b);

/** Side-by-side ledgers for one policy: policy system on the left, billing on the right, disagreements in red. */
export default function PolicyLedger({ policyNumber, findings, onClose }) {
  const dialog = useRef(null);
  const [state, setState] = useState({ loading: true });

  useEffect(() => {
    dialog.current?.showModal();
    let cancelled = false;
    Promise.all([api.policy(policyNumber), api.billingAccount(policyNumber)])
      .then(([policy, billing]) => !cancelled && setState({ policy, billing }))
      .catch((e) => !cancelled && setState({ error: e.message }));
    return () => { cancelled = true; };
  }, [policyNumber]);

  const { loading, error, policy, billing } = state;
  const policyRows = policy?.transactions ?? [];
  const billingRows = billing?.transactions ?? [];
  const sequences = [...new Set([...policyRows.map((t) => t.sequence), ...billingRows.map((t) => t.sequence)])].sort((a, b) => a - b);
  const policyFindings = findings.filter((f) => f.policyNumber === policyNumber);

  return (
    <dialog ref={dialog} className="ledger" onClose={onClose} aria-labelledby="ledger-title">
      <header className="ledger-head">
        <div>
          <h2 id="ledger-title">{policyNumber}</h2>
          {policy && (
            <p className="ledger-meta">
              {policy.insuredName}, {policy.status === 'Cancelled' ? 'cancelled' : 'in force'}, {Math.round(policy.commissionRate * 1000) / 10}% commission
            </p>
          )}
        </div>
        <button type="button" className="close" onClick={() => dialog.current.close()}>Close</button>
      </header>

      {policyFindings.length > 0 && (
        <ul className="ledger-findings">
          {policyFindings.map((f, i) => (
            <li key={i}><strong>{REASON_BY_CODE[f.reasonCode]?.label ?? f.reasonCode}.</strong> {f.detail}</li>
          ))}
        </ul>
      )}

      {loading && <p className="empty">Loading both ledgers…</p>}
      {error && <p className="error">{error}</p>}

      {!loading && !error && (
        <div className="table-wrap">
          <table className="ledger-table">
            <thead>
              <tr>
                <th scope="col" rowSpan="2" className="num">Seq</th>
                <th scope="col" rowSpan="2">Type</th>
                <th scope="colgroup" colSpan="3" className="side policy-ink split">Policy system</th>
                <th scope="colgroup" colSpan="3" className="side billing-ink">Billing</th>
              </tr>
              <tr>
                <th scope="col" className="num policy-ink">Premium</th>
                <th scope="col" className="num policy-ink">Fee</th>
                <th scope="col" className="policy-ink split">Delivery</th>
                <th scope="col" className="num billing-ink">Premium</th>
                <th scope="col" className="num billing-ink">Fee</th>
                <th scope="col" className="num billing-ink">Commission</th>
              </tr>
            </thead>
            <tbody>
              {sequences.map((seq) => {
                const p = policyRows.find((t) => t.sequence === seq);
                const b = billingRows.find((t) => t.sequence === seq);
                const premiumOff = p && b && !same(p.premiumChange, b.premium);
                const feeOff = p && b && !same(p.feeAmount, b.feeAmount);
                return (
                  <tr key={seq}>
                    <td className="num">{seq}</td>
                    <td>{(p ?? b).type.replace(/([a-z])([A-Z])/g, '$1 $2')}</td>
                    {p ? (
                      <>
                        <td className={`num${premiumOff ? ' off' : ''}`}>{formatMoney(p.premiumChange)}</td>
                        <td className={`num${feeOff ? ' off' : ''}`}>{formatMoney(p.feeAmount)}</td>
                        <td className={`split${p.syncStatus === 'Sent' ? '' : ' off'}`}>{p.syncStatus === 'Sent' ? 'Delivered' : p.syncStatus}</td>
                      </>
                    ) : (
                      <td colSpan="3" className="missing split">Not in the policy system</td>
                    )}
                    {b ? (
                      <>
                        <td className={`num${premiumOff ? ' off' : ''}`}>{formatMoney(b.premium)}</td>
                        <td className={`num${feeOff ? ' off' : ''}`}>{formatMoney(b.feeAmount)}</td>
                        <td className="num">{formatMoney(b.commissionAmount)}</td>
                      </>
                    ) : (
                      <td colSpan="3" className="missing">Not in billing</td>
                    )}
                  </tr>
                );
              })}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row" colSpan="2">Totals</th>
                <td className={`num${policy && billing && !same(policy.writtenPremium, billing.totalPremium) ? ' off' : ''}`}>
                  {formatMoney(policy?.writtenPremium)}
                </td>
                <td />
                <td className="hint split">Expected commission {formatMoney(policy?.expectedCommission)}</td>
                <td className={`num${policy && billing && !same(policy.writtenPremium, billing.totalPremium) ? ' off' : ''}`}>
                  {formatMoney(billing?.totalPremium)}
                </td>
                <td className="num">{formatMoney(billing?.totalFees)}</td>
                <td className={`num${policy && billing && !same(policy.expectedCommission, billing.totalCommission) ? ' off' : ''}`}>
                  {formatMoney(billing?.totalCommission)}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
    </dialog>
  );
}
