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
