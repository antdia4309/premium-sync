// Thin wrappers over the three services. Paths are proxied by vite.config.js.

const SERVICES = {
  '/policy': 'policy service (port 5001)',
  '/billing': 'billing service (port 5002)',
  '/recon': 'reconciliation service (port 5003)',
};

async function request(path, options = {}) {
  const service = SERVICES[Object.keys(SERVICES).find((p) => path.startsWith(p))];
  let res;
  try {
    res = await fetch(path, options);
  } catch {
    throw new Error(`Can't reach the ${service}. Start everything with ./run.sh.`);
  }
  if (res.status === 404) return null;
  if (!res.ok) {
    let body = null;
    try { body = await res.json(); } catch { /* not JSON: the dev server couldn't reach the service */ }
    if (!body && res.status >= 500) throw new Error(`The ${service} isn't running. Start everything with ./run.sh.`);
    throw new Error(body?.detail || body?.title || `The ${service} returned ${res.status}.`);
  }
  return res.status === 204 ? null : res.json();
}

const post = (path) => request(path, { method: 'POST' });

export const api = {
  runs: () => request('/recon/api/recon/runs?take=30'),
  findings: (runId) => request(`/recon/api/recon/runs/${runId}/findings`),
  runReconciliation: () => post('/recon/api/recon/runs'),
  injectAll: () => post('/recon/api/dev/inject/all'),
  seed: (count = 100) => post(`/policy/api/dev/seed?count=${count}`),
  policy: (number) => request(`/policy/api/policies/${encodeURIComponent(number)}`),
  billingAccount: (number) => request(`/billing/api/billing/accounts/${encodeURIComponent(number)}`),
};
