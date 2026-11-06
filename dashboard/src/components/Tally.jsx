import { REASONS } from '../reasons.js';

/** Counts by reason code. Each row filters the findings list. */
export default function Tally({ byReason, active, onSelect }) {
  const max = Math.max(1, ...REASONS.map((r) => byReason?.[r.code] ?? 0));
  return (
    <section className="tally" aria-labelledby="tally-heading">
      <h2 id="tally-heading">By reason</h2>
      <ul>
        {REASONS.map((r) => {
          const count = byReason?.[r.code] ?? 0;
          const isActive = active === r.code;
          return (
            <li key={r.code}>
              <button
                type="button"
                className={`tally-row${count ? ' has-findings' : ''}${isActive ? ' active' : ''}`}
                onClick={() => onSelect(isActive ? null : r.code)}
                aria-pressed={isActive}
                disabled={!count && !isActive}
                title={r.help}
              >
                <span className="tally-label">{r.label}</span>
                <span className="tally-bar" aria-hidden="true">
                  <span style={{ width: `${(count / max) * 100}%` }} />
                </span>
                <span className="tally-count">{count}</span>
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
