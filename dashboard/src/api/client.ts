import type {
  AuthUser,
  Branch,
  Company,
  CreateScheduleRequest,
  DocumentGroup,
  DocumentSummary,
  ExecutionSummary,
  LoginResponse,
  ManualIntegrationRequest,
  ManualIntegrationResult,
  Schedule,
  TraceResponse,
} from '../types';

const BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5200';
const TOKEN_KEY = 'fiscalhub.token';

// Token no localStorage (Bearer). Trade-off anotado no ADR: produção = cookie httpOnly.
export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string | null) => (t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY)),
};

// Quando a API responde 401, o token caiu/expirou: limpa e avisa a app pra voltar ao login.
let onUnauthorized: (() => void) | null = null;
export function setUnauthorizedHandler(fn: (() => void) | null) {
  onUnauthorized = fn;
}

function authHeaders(): Record<string, string> {
  const t = tokenStore.get();
  return t ? { Authorization: `Bearer ${t}` } : {};
}

function handleUnauthorized() {
  tokenStore.set(null);
  onUnauthorized?.();
}

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}${path}`, { headers: authHeaders() });
  if (res.status === 401) {
    handleUnauthorized();
    throw new Error('Sessão expirada.');
  }
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body),
  });
  if (res.status === 401) {
    handleUnauthorized();
    throw new Error('Sessão expirada.');
  }
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

export const api = {
  login: async (email: string, password: string): Promise<LoginResponse> => {
    const res = await fetch(`${BASE}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
    if (res.status === 401) {
      throw new Error('E-mail ou senha inválidos.');
    }
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }
    return (await res.json()) as LoginResponse;
  },
  me: () => getJson<AuthUser>('/auth/me'),
  groups: () => getJson<DocumentGroup[]>('/groups'),
  groupDocuments: (company: string, branch: string, date: string) =>
    getJson<DocumentSummary[]>(
      `/groups/${encodeURIComponent(company)}/${encodeURIComponent(branch)}/${encodeURIComponent(date)}/documents`,
    ),
  documents: () => getJson<DocumentSummary[]>('/documents'),
  trace: (tenantId: string, naturalKey: string) =>
    getJson<TraceResponse>(`/trace/${encodeURIComponent(tenantId)}/${encodeURIComponent(naturalKey)}`),
  info: () => getJson<{ environment: string }>('/info'),
  // Download com Bearer: baixa como blob (um <a href> não mandaria o token).
  downloadTrace: async (tenantId: string, naturalKey: string): Promise<void> => {
    const res = await fetch(
      `${BASE}/documents/${encodeURIComponent(tenantId)}/${encodeURIComponent(naturalKey)}/download`,
      { headers: authHeaders() },
    );
    if (res.status === 401) {
      handleUnauthorized();
      throw new Error('Sessão expirada.');
    }
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${naturalKey}.zip`;
    a.click();
    URL.revokeObjectURL(url);
  },
  companies: () => getJson<Company[]>('/companies'),
  branches: (code: string) => getJson<Branch[]>(`/companies/${encodeURIComponent(code)}/branches`),
  runManualIntegration: (body: ManualIntegrationRequest) =>
    postJson<ManualIntegrationResult>('/integrations/manual', body),
  executions: () => getJson<ExecutionSummary[]>('/executions'),
  schedules: () => getJson<Schedule[]>('/schedules'),
  createSchedule: (body: CreateScheduleRequest) => postJson<{ id: number }>('/schedules', body),
  deactivateSchedule: async (id: number): Promise<void> => {
    const res = await fetch(`${BASE}/schedules/${id}/deactivate`, { method: 'POST', headers: authHeaders() });
    if (res.status === 401) {
      handleUnauthorized();
    }
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }
  },
};
