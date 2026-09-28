// Espelha os DTOs do backend (DocumentSummary / GET /trace). Tipar aqui pega erro de contrato cedo.

export type IntegrationStatus =
  | 'Pending'
  | 'Submitted'
  | 'Confirmed'
  | 'IntegrationError'
  | 'Unconfirmed'
  | 'DeadLettered'
  | 'Ignored';

export interface DocumentSummary {
  tenantId: string;
  naturalKey: string;
  type: string;
  status: IntegrationStatus;
  attempts: number;
  externalId?: string | null;
  reason?: string | null;
  number?: string | null;
  model?: string | null;
  updatedAt: string;
}

// GET /trace devolve { "<caminho>": <conteudo> } — JSON aninhado ou string (o XML cru).
export type TraceResponse = Record<string, unknown>;

// As fotos ja categorizadas: fonte, dominio, payload de destino e as respostas da plataforma (ADR-0027) —
// a do ultimo envio e a da ultima consulta de status, cada uma um envelope ja redigido.
export interface DocumentTrace {
  source?: string;
  domain?: unknown;
  destination?: { name: string; payload: unknown };
  responses?: { submit?: unknown; status?: unknown };
}

// Diretorio de empresas/filiais (GET /companies, /companies/{code}/branches) — dropdowns da manual.
export interface Company {
  code: string;
  name: string;
}

export interface Branch {
  code: string;
  name: string;
}

// Integracao manual (POST /integrations/manual). branchCode null = todas as filiais.
export interface ManualIntegrationRequest {
  companyCode: string;
  branchCode: string | null;
  periodStart: string;
  periodEnd: string;
  documentNumber?: string | null;   // preenchido = uma nota específica (nNF)
}

export interface ManualIntegrationResult {
  discovered: number;
  keys: string[];
}

// Usuário autenticado (GET /auth/me e POST /auth/login).
export interface AuthUser {
  email: string;
  name: string;
  tenantId: string;
  role: string;
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: AuthUser;
}

// Administração de usuários do tenant (GET/POST/PUT /users) — tela de Usuários (Admin).
export type UserRole = 'Admin' | 'Viewer';

export interface AdminUser {
  id: number;
  email: string;
  name: string;
  role: UserRole;
  active: boolean;
}

export interface CreateUserRequest {
  email: string;
  name: string;
  role: UserRole;
  password: string;
}

export interface UpdateUserRequest {
  name?: string | null;
  role?: UserRole | null;
  active?: boolean | null;
}

// Cadastro (fino) do tenant corrente (GET/PUT /tenant).
export interface TenantInfo {
  tenantId: string;
  name: string;
  cnpj?: string | null;
  active: boolean;
}

// Perfil de conector do tenant (GET/PUT /connector) — tela admin de Conectores. A leitura vem sem os
// segredos e sem as referências: `secrets` diz, por caminho (`outbound.sandbox.clientSecret`), se o
// segredo está no cofre e quando foi gravado (ADR-0027).
export interface SecretStatus {
  configured: boolean;
  updatedOn: string | null;
}

export interface ConnectorProfile {
  tenantId: string;
  environment: string;
  inboundAdapter: string;
  inboundSettings: string;
  outboundAdapter: string;
  outboundSettings: string;
  supportAdapter: string | null;
  supportSettings: string;
  secrets: Record<string, SecretStatus>;
}

export interface ConnectorProfileRequest {
  environment: string;
  inboundAdapter: string;
  inboundSettings: string | null;
  outboundAdapter: string;
  outboundSettings: string | null;
}

// Modo de uma execução/agendamento (espelha IntegrationMode do backend).
export type IntegrationModeName = 'Manual' | 'ScheduledDaily' | 'ScheduledOnce';

// Execução de integração (GET /executions) — linha da tela de Agendamentos.
export interface ExecutionSummary {
  id: number;
  mode: IntegrationModeName;
  companyCode: string;
  branchCode?: string | null;
  periodStart: string;
  periodEnd: string;
  discoveredCount: number;
  runAt: string;
}

// Agendamento cadastrado (GET /schedules).
export interface Schedule {
  id: number;
  mode: IntegrationModeName;
  tenantId: string;
  companyCode: string;
  branchCode?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
  nextRunAt: string;
  active: boolean;
}

// Corpo do POST /schedules. Diária: timeOfDay "HH:mm". Única: runAt + periodStart/periodEnd.
export interface CreateScheduleRequest {
  mode: 'ScheduledDaily' | 'ScheduledOnce';
  companyCode: string;
  branchCode: string | null;
  timeOfDay?: string | null;
  runAt?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
}

// Grupo (empresa/filial/dia) com contagens — a linha principal do dashboard.
export interface DocumentGroup {
  companyCode: string;
  branchCode: string;
  referenceDate: string;
  type: string;
  trigger: string; // modo da integração: RealTime | Manual | ScheduledDaily | ScheduledOnce
  total: number;
  finalizadas: number;
  emProcessamento: number;
  comErro: number;
}
