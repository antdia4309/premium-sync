import { formatWhen } from '../reasons.js';

export default function RunHistory({ runs, selectedId, onSelect }) {
  if (!runs.length) return null;
  return (
    <section className="history" aria-labelledby="history-heading">
      <h2 id="history-heading">Earlier runs</h2>
      <ol>
        {runs.map((run) => (
          <li key={run.runId}>
            <button
              type="button"
              className={run.runId === selectedId ? 'active' : ''}
              aria-current={run.runId === selectedId ? 'true' : undefined}
              onClick={() => onSelect(run.runId)}
            >
              <span>Run {run.runId}</span>
              <span className="history-when">{formatWhen(run.startedAtUtc)}</span>
              <span className={run.findingCount ? 'history-count bad' : 'history-count good'}>
                {run.findingCount ? run.findingCount : 'Clean'}
              </span>
            </button>
          </li>
        ))}
      </ol>
    </section>
  );
}
