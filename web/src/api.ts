import type {
  AppConfig,
  RuleSet,
  RunResult,
  RunSummary,
  Scenario
} from './types';

async function req<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
    ...init
  });
  if (!res.ok) {
    let detail = res.statusText;
    try {
      const j = await res.json();
      detail = j.error ?? JSON.stringify(j);
    } catch {
      /* keep status text */
    }
    throw new Error(`${res.status}: ${detail}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const api = {
  config: () => req<AppConfig>('/api/config'),

  rules: () => req<RuleSet[]>('/api/rules'),
  createRule: (body: Partial<RuleSet>) =>
    req<RuleSet>('/api/rules', { method: 'POST', body: JSON.stringify(body) }),

  scenarios: (ruleSetId?: string) =>
    req<Scenario[]>(`/api/scenarios${ruleSetId ? `?ruleSetId=${ruleSetId}` : ''}`),
  createScenario: (body: unknown) =>
    req<Scenario>('/api/scenarios', { method: 'POST', body: JSON.stringify(body) }),
  updateScenario: (id: string, body: unknown) =>
    req<Scenario>(`/api/scenarios/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteScenario: (id: string) =>
    req<void>(`/api/scenarios/${id}`, { method: 'DELETE' }),

  runs: () => req<RunSummary[]>('/api/runs'),
  startRun: (body: unknown) =>
    req<RunSummary>('/api/runs', { method: 'POST', body: JSON.stringify(body) }),
  run: (id: string) => req<RunSummary>(`/api/runs/${id}`),
  results: (id: string) => req<RunResult[]>(`/api/runs/${id}/results`),
  cancelRun: (id: string) =>
    req<void>(`/api/runs/${id}/cancel`, { method: 'POST' })
};
