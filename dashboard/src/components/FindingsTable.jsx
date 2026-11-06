import { REASON_BY_CODE, formatDifference, formatMoney } from '../reasons.js';

export default function FindingsTable({ findings, reason, query, onQuery, onClearReason, onOpenPolicy }) {
  const q = query.trim().toUpperCase();
  const rows = findings.filter((f) => (!reason || f.reasonCode === reason) && (!q || f.policyNumber.includes(q)));

  return (
    <section className="findings" aria-labelledby="findings-heading">
      <div className="findings-head">
        <h2 id="findings-heading">
          {reason ? REASON_BY_CODE[reason].label : 'All findings'}
          {reason && (
            <button type="button" className="link" onClick={onClearReason}>Show all</button>
          )}
        </h2>
        <label className="search">
          <span className="visually-hidden">Find a policy</span>
          <input type="search" placeholder="Find a policy, e.g. PS-000012" value={query} onChange={(e) => onQuery(e.target.value)} />
        </label>
      </div>

      {rows.length === 0 ? (
        <p className="empty">{findings.length ? 'No findings match this filter.' : 'Nothing to reconcile in this run.'}</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Policy</th>
                <th scope="col">What’s wrong</th>
                <th scope="col" className="num">Seq</th>
                <th scope="col" className="num policy-ink">Policy</th>
                <th scope="col" className="num billing-ink">Billing</th>
                <th scope="col" className="num">Difference</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((f, i) => (
                <tr key={`${f.policyNumber}-${f.reasonCode}-${f.sequence ?? 'p'}-${i}`}>
                  <td>
                    <button type="button" className="policy-link" onClick={() => onOpenPolicy(f.policyNumber)}>
                      {f.policyNumber}
                    </button>
                  </td>
                  <td>
                    <span className="reason">{REASON_BY_CODE[f.reasonCode]?.label ?? f.reasonCode}</span>
                    <span className="detail">{f.detail}</span>
                  </td>
                  <td className="num">{f.sequence ?? ''}</td>
                  <td className="num policy-ink">{formatMoney(f.policyValue)}</td>
                  <td className="num billing-ink">{formatMoney(f.billingValue)}</td>
                  <td className="num red-ink">{formatDifference(f.difference)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
