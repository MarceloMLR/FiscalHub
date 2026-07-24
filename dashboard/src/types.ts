// Espelha os DTOs do backend (DocumentSummary / GET /trace). Tipar aqui pega erro de contrato cedo.

export type IntegrationStatus =
  | 'Pending'
  | 'Submitted'
  | 'Confirmed'
  | 'IntegrationError'
  | 'Unconfirmed'
  | 'DeadLettered';

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

// As tres fotos ja categorizadas.
export interface DocumentTrace {
  source?: string;
  domain?: unknown;
  destination?: { name: string; payload: unknown };
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
  tenantId?: string | null;
}

export interface ManualIntegrationResult {
  discovered: number;
  keys: string[];
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
  tenantId?: string | null;
}

// Grupo (empresa/filial/dia) com contagens — a linha principal do dashboard.
export interface DocumentGroup {
  companyCode: string;
  branchCode: string;
  referenceDate: string;
  type: string;
  total: number;
  finalizadas: number;
  emProcessamento: number;
  comErro: number;
}
