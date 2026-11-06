import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from './api.js';
import { formatWhen, REASON_BY_CODE } from './reasons.js';
import Tally from './components/Tally.jsx';
import RunHistory from './components/RunHistory.jsx';
import FindingsTable from './components/FindingsTable.jsx';
import PolicyLedger from './components/PolicyLedger.jsx';

const plural = (n, one, many = `${one}s`) => `${n.toLocaleString('en-US')} ${n === 1 ? one : many}`;

export default function App() {
  const [runs, setRuns] = useState(null);           // null = loading
  const [selectedId, setSelectedId] = useState(null);
  const [findings, setFindings] = useState([]);
  const [reason, setReason] = useState(null);
  const [query, setQuery] = useState('');
  const [openPolicy, setOpenPolicy] = useState(null);
  const [busy, setBusy] = useState(null);
  const [notice, setNotice] = useState(null);

  const loadRuns = useCallback(async (selectNewest = false) => {
    const list = (await api.runs()) ?? [];
    setRuns(list);
    setSelectedId((current) => (selectNewest || current === null ? list[0]?.runId ?? null : current));
    return list;
  }, []);

  useEffect(() => {
    loadRuns().catch((e) => { setRuns([]); setNotice({ kind: 'error', text: e.message }); });
  }, [loadRuns]);

  useEffect(() => {
    if (selectedId === null) { setFindings([]); return; }
    api.findings(selectedId)
      .then((f) => setFindings(f ?? []))
      .catch((e) => setNotice({ kind: 'error', text: e.message }));
  }, [selectedId]);

  const run = runs?.find((r) => r.runId === selectedId) ?? null;
  const isLatest = run && runs[0]?.runId === run.runId;
  const policiesAffected = useMemo(() => new Set(findings.map((f) => f.policyNumber)).size, [findings]);

  async function act(name, work) {
    setBusy(name);
    setNotice(null);
    try { await work(); }
    catch (e) { setNotice({ kind: 'error', text: e.message }); }
    finally { setBusy(null); }
  }

  const seed = () => act('seed', async () => {
    const r = await api.seed(100);
    setNotice({ kind: 'info', text: `Added ${plural(r.policies, 'policy', 'policies')} with ${plural(r.endorsements, 'endorsement')} and ${plural(r.cancellations, 'cancellation')}. Run a reconciliation to check them.` });
  });

  const inject = () => act('inject', async () => {
    const injected = await api.injectAll();
    setNotice({
      kind: 'info',
      text: `Corrupted ${plural(injected.length, 'policy', 'policies')}, one for each kind of problem. Run a reconciliation to find them.`,
      items: injected.map((i) => `${i.policyNumber}: ${REASON_BY_CODE[i.kind]?.label ?? i.kind}. ${i.change}`),
    });
  });

  const reconcile = () => act('reconcile', async () => {
    await api.runReconciliation();
    setReason(null);
    await loadRuns(true);
  });

  return (
    <div className="page">
      <header className="masthead">
        <div>
          <p className="product">Premium Sync</p>
          <h1>Reconciliation</h1>
        </div>
        <div className="actions">
          <button type="button" className="quiet" onClick={seed} disabled={!!busy}>
            {busy === 'seed' ? 'Adding policies…' : 'Add 100 test policies'}
          </button>
          <button type="button" className="quiet" onClick={inject} disabled={!!busy} title="Development only: corrupts the data on purpose">
            {busy === 'inject' ? 'Injecting…' : 'Inject problems'}
          </button>
          <button type="button" className="primary" onClick={reconcile} disabled={!!busy}>
            {busy === 'reconcile' ? 'Reconciling…' : 'Run reconciliation'}
          </button>
        </div>
      </header>

      {notice && (
        <div className={`notice ${notice.kind}`} role={notice.kind === 'error' ? 'alert' : 'status'}>
          <p>{notice.text}</p>
          {notice.items && <ul>{notice.items.map((t) => <li key={t}>{t}</li>)}</ul>}
          <button type="button" className="link" onClick={() => setNotice(null)}>Dismiss</button>
        </div>
      )}

      <section className="verdict" aria-live="polite">
        {runs === null ? (
          <p className="verdict-line">Loading…</p>
        ) : !run ? (
          <>
            <p className="verdict-line">No reconciliation yet.</p>
            <p className="verdict-sub">Add some test policies, then run a reconciliation to compare the policy system with billing.</p>
          </>
        ) : (
          <>
            <p className={`verdict-line ${run.findingCount ? 'disagree' : 'agree'}`}>
              {run.findingCount
                ? `${plural(run.findingCount, 'discrepancy', 'discrepancies')} across ${plural(policiesAffected, 'policy', 'policies')}.`
                : 'Policy and billing agree.'}
            </p>
            <p className="verdict-sub">
              Run {run.runId} checked {plural(run.policiesChecked, 'policy', 'policies')} and {plural(run.transactionsChecked, 'transaction')} on {formatWhen(run.startedAtUtc)}.
              {!isLatest && <> This is an earlier run. <button type="button" className="link" onClick={() => setSelectedId(runs[0].runId)}>See the latest</button></>}
            </p>
          </>
        )}
      </section>

      {run && (
        <main className="workspace">
          <aside className="sidebar">
            <Tally byReason={run.byReason} active={reason} onSelect={setReason} />
            <RunHistory runs={runs} selectedId={selectedId} onSelect={(id) => { setSelectedId(id); setReason(null); }} />
          </aside>
          <FindingsTable
            findings={findings}
            reason={reason}
            query={query}
            onQuery={setQuery}
            onClearReason={() => setReason(null)}
            onOpenPolicy={setOpenPolicy}
          />
        </main>
      )}

      {openPolicy && <PolicyLedger policyNumber={openPolicy} findings={findings} onClose={() => setOpenPolicy(null)} />}
    </div>
  );
}
