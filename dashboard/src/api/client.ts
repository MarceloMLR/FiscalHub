import type {
  AdminUser,
  AutomaticPanel,
  CredentialTestResult,
  AuthUser,
  Branch,
  Company,
  ConnectorProfile,
  ConnectorProfileRequest,
  CreateScheduleRequest,
  CreateUserRequest,
  DocumentGroup,
  DocumentReading,
  DocumentSummary,
  ExecutionSummary,
  InfoResponse,
  LoginResponse,
  ManualIntegrationRequest,
  ManualIntegrationResult,
  ModelTotals,
  Schedule,
  TenantInfo,
  TraceResponse,
  UpdateUserRequest,
} from '../types';
import { resolveApiBase } from './config';

// URL da API resolvida em RUNTIME (por host), não assada no build — um único bundle serve todos os
// clientes: acme.fiscalhub.com fala com a API da ACME, tmsa.fiscalhub.com com a da TMSA. Ver ADR-0020.
const BASE = resolveApiBase();
const TOKEN_KEY = 'fiscalhub.token';

// Token Bearer. "Manter conectado" = localStorage (persiste ao fechar o navegador);
// desmarcado = sessionStorage (some ao fechar a aba). Trade-off anotado no ADR: produção = cookie httpOnly.
export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY),
  set: (t: string | null, remember = true) => {
    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TOKEN_KEY);
    if (t) {
      (remember ? localStorage : sessionStorage).setItem(TOKEN_KEY, t);
    }
  },
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

async function sendJson<T>(method: 'POST' | 'PUT', path: string, body: unknown): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    method,
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

// Como getJson, mas o erro leva a mensagem do backend ({ message }): o diretório responde 404 quando o ERP do tenant não
// tem diretório e 502 quando a leitura da origem falhou, e a tela mostra o motivo no lugar da lista.
async function getJsonWithMessage<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}${path}`, { headers: authHeaders() });
  if (res.status === 401) {
    handleUnauthorized();
    throw new Error('Sessão expirada.');
  }
  if (!res.ok) {
    const data = (await res.json().catch(() => ({}))) as { message?: string };
    throw new Error(data.message ?? `${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

const postJson = <T>(path: string, body: unknown) => sendJson<T>('POST', path, body);
const putJson = <T>(path: string, body: unknown) => sendJson<T>('PUT', path, body);

// Como sendJson, mas propaga a mensagem de erro do backend ({ message }) e tolera 204 sem corpo.
async function sendAdmin<T>(method: 'POST' | 'PUT', path: string, body: unknown): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body),
  });
  if (res.status === 401) {
    handleUnauthorized();
    throw new Error('Sessão expirada.');
  }
  if (!res.ok) {
    const data = (await res.json().catch(() => ({}))) as { message?: string };
    throw new Error(data.message ?? `${res.status} ${res.statusText}`);
  }
  if (res.status === 204) {
    return undefined as T;
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
  forgotPassword: (email: string) =>
    postJson<{ message: string; devToken?: string | null }>('/auth/forgot-password', { email }),
  resetPassword: async (token: string, newPassword: string): Promise<{ message: string }> => {
    const res = await fetch(`${BASE}/auth/reset-password`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, newPassword }),
    });
    const data = (await res.json().catch(() => ({}))) as { message?: string };
    if (!res.ok) {
      throw new Error(data.message ?? `${res.status} ${res.statusText}`);
    }
    return { message: data.message ?? 'Senha redefinida.' };
  },
  // A tabela, com a mesma janela de dias da execução (aaaa-mm-dd, inclusive) e o mesmo modelo dos cards (D15).
  groups: (from: string, to: string, model: string | null) => {
    const query = new URLSearchParams({ from, to });
    if (model) {
      query.set('model', model);
    }
    return getJson<DocumentGroup[]>(`/groups?${query}`);
  },
  // As contagens dos cards por modelo, na janela de dias da execução (aaaa-mm-dd), inclusive.
  groupTotals: (from: string, to: string) =>
    getJson<ModelTotals[]>(`/groups/totals?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  // As notas da linha inteira: empresa, filial e dia da execução na rota, e o tipo, o modelo, o modo e o período na query
  // (period = none na automática, ou aaaa-mm-dd_aaaa-mm-dd).
  groupDocuments: (group: Pick<DocumentGroup, 'companyCode' | 'branchCode' | 'executedOn' | 'type' | 'model' | 'trigger'>, period: string) => {
    const query = new URLSearchParams({ type: group.type, trigger: group.trigger, period });
    if (group.model) {
      query.set('model', group.model);
    }
    return getJson<DocumentSummary[]>(
      `/groups/${encodeURIComponent(group.companyCode)}/${encodeURIComponent(group.branchCode)}/${encodeURIComponent(group.executedOn)}/documents?${query}`,
    );
  },
  documents: () => getJson<DocumentSummary[]>('/documents'),
  // As fotos cruas: só para os papéis que as veem (403 para os demais). Usado pelo modal do JSON.
  trace: (tenantId: string, naturalKey: string) =>
    getJson<TraceResponse>(`/trace/${encodeURIComponent(tenantId)}/${encodeURIComponent(naturalKey)}`),
  // A leitura do desfecho: a lista de campos da recusa e as omissões, para qualquer papel. 404 = sem fotos.
  reading: (tenantId: string, naturalKey: string) =>
    getJson<DocumentReading>(`/documents/${encodeURIComponent(tenantId)}/${encodeURIComponent(naturalKey)}/reading`),
  // O ambiente, o selo e os módulos da barra lateral (ver InfoResponse).
  info: () => getJson<InfoResponse>('/info'),
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
  // O diretório do tenant logado. 404 = o ERP do tenant não tem diretório; 502 = a leitura da origem falhou. O erro leva o
  // motivo do servidor.
  companies: () => getJsonWithMessage<Company[]>('/companies'),
  branches: (code: string) => getJsonWithMessage<Branch[]>(`/companies/${encodeURIComponent(code)}/branches`),
  // 409 = o ERP do tenant não tem descoberta por período; 502 = a leitura da origem falhou. O erro leva o motivo.
  runManualIntegration: (body: ManualIntegrationRequest) =>
    sendAdmin<ManualIntegrationResult>('POST', '/integrations/manual', body),
  executions: () => getJson<ExecutionSummary[]>('/executions'),
  schedules: () => getJson<Schedule[]>('/schedules'),
  createSchedule: (body: CreateScheduleRequest) => postJson<{ id: number }>('/schedules', body),
  updateSchedule: (id: number, body: CreateScheduleRequest) => putJson<{ id: number }>(`/schedules/${id}`, body),
  deactivateSchedule: async (id: number): Promise<void> => {
    const res = await fetch(`${BASE}/schedules/${id}/deactivate`, { method: 'POST', headers: authHeaders() });
    if (res.status === 401) {
      handleUnauthorized();
    }
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }
  },
  // Exclusão física; as execuções que o agendamento disparou ficam no histórico.
  deleteSchedule: async (id: number): Promise<void> => {
    const res = await fetch(`${BASE}/schedules/${id}`, { method: 'DELETE', headers: authHeaders() });
    if (res.status === 401) {
      handleUnauthorized();
    }
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }
  },
  reactivateSchedule: async (id: number): Promise<void> => {
    const res = await fetch(`${BASE}/schedules/${id}/reactivate`, { method: 'POST', headers: authHeaders() });
    if (res.status === 401) {
      handleUnauthorized();
    }
    if (!res.ok) {
      throw new Error(`${res.status} ${res.statusText}`);
    }
  },
  // 404 = a nota não está mais na origem; 502 = a leitura da origem falhou. O erro leva o motivo do servidor.
  reprocess: async (tenantId: string, naturalKey: string): Promise<void> => {
    const res = await fetch(
      `${BASE}/documents/${encodeURIComponent(tenantId)}/${encodeURIComponent(naturalKey)}/reprocess`,
      { method: 'POST', headers: authHeaders() },
    );
    if (res.status === 401) {
      handleUnauthorized();
    }
    if (!res.ok) {
      const data = (await res.json().catch(() => ({}))) as { message?: string };
      throw new Error(data.message ?? `${res.status} ${res.statusText}`);
    }
  },
  // ---- Administração de usuários / tenant (Admin). Erros carregam a mensagem do backend. ----
  users: () => getJson<AdminUser[]>('/users'),
  createUser: (body: CreateUserRequest) => sendAdmin<AdminUser>('POST', '/users', body),
  updateUser: (id: number, body: UpdateUserRequest) => sendAdmin<AdminUser>('PUT', `/users/${id}`, body),
  resetUserPassword: (id: number, newPassword: string) =>
    sendAdmin<void>('POST', `/users/${id}/reset-password`, { newPassword }),
  tenant: () => getJson<TenantInfo>('/tenant'),
  saveTenant: (body: { name: string; cnpj: string | null }) => sendAdmin<TenantInfo>('PUT', '/tenant', body),
  // Estimativa do tamanho dos logs (zips) das notas selecionadas, pra tela mostrar o disponível.
  estimateTicketLogs: (naturalKeys: string[]) =>
    sendAdmin<{ logsBytes: number; limitBytes: number }>('POST', '/support/tickets/estimate', { naturalKeys }),
  // Abrir chamado de suporte (multipart): logs das notas anexados automaticamente + anexos extras opcionais.
  openTicket: async (
    subject: string,
    description: string,
    naturalKeys: string[],
    files: File[] = [],
  ): Promise<{ ticketId: string; url: string | null }> => {
    const fd = new FormData();
    fd.append('subject', subject);
    fd.append('description', description);
    naturalKeys.forEach((k) => fd.append('naturalKeys', k));
    files.forEach((f) => fd.append('files', f));
    const res = await fetch(`${BASE}/support/tickets`, { method: 'POST', headers: authHeaders(), body: fd });
    if (res.status === 401) {
      handleUnauthorized();
      throw new Error('Sessão expirada.');
    }
    if (!res.ok) {
      const data = (await res.json().catch(() => ({}))) as { message?: string };
      throw new Error(data.message ?? `${res.status} ${res.statusText}`);
    }
    return (await res.json()) as { ticketId: string; url: string | null };
  },
  connector: () => getJson<ConnectorProfile>('/connector'),
  // O painel da integração automática (só Admin). 404 = o adapter de entrada não varre.
  automaticPanel: () => getJson<AutomaticPanel>('/connector/automatic'),
  // Volta a marca para o instante (ISO). 400 = não é anterior à marca ou está no futuro; 409 = o coletor está lendo,
  // ou não há marca. As mensagens vêm do servidor.
  // Testa a credencial GRAVADA de um lado (na saída, de um ambiente). A requisição não leva credencial nenhuma.
  testConnector: (side: 'inbound' | 'outbound', environment?: 'Sandbox' | 'Production') =>
    sendAdmin<CredentialTestResult>('POST', '/connector/test', { side, environment }),
  rewindAutomatic: (watermark: string) =>
    sendAdmin<{ watermark: string; message: string }>('POST', '/connector/automatic/rewind', { watermark }),
  saveConnector: async (body: ConnectorProfileRequest): Promise<void> => {
    const res = await fetch(`${BASE}/connector`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify(body),
    });
    if (res.status === 401) {
      handleUnauthorized();
      throw new Error('Sessão expirada.');
    }
    if (!res.ok) {
      // 400 (settings recusadas) e 502 (cofre) trazem { message }, que cita o campo e nunca o valor.
      const data = (await res.json().catch(() => ({}))) as { message?: string };
      throw new Error(data.message ?? `${res.status} ${res.statusText}`);
    }
  },
};
