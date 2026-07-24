import type { DocumentSummary, TraceResponse } from '../types';

const BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5200';

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}${path}`);
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

export const api = {
  documents: () => getJson<DocumentSummary[]>('/documents'),
  trace: (tenantId: string, naturalKey: string) =>
    getJson<TraceResponse>(`/trace/${encodeURIComponent(tenantId)}/${encodeURIComponent(naturalKey)}`),
  info: () => getJson<{ environment: string }>('/info'),
};
