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
  reprocessings: number; // quantas vezes o reprocesso foi aceito; não zera no reenvio, ao contrário das consultas
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

// A leitura do desfecho (GET /documents/{tenant}/{chave}/reading): o que a primeira vista do detalhe usa, tirado das
// fotos no servidor, e nada mais delas. Aberta a qualquer papel; as fotos cruas (o /trace) so para Admin.
export interface FieldRejection {
  path: string; // o caminho como a plataforma o escreveu (ex.: itens[0].Item.TipoItem)
  messages: string[]; // as mensagens da plataforma, como vieram
}

export interface DocumentReading {
  fields: FieldRejection[];
  omissions: string[];
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
// Os módulos do tenant (GET /info, GET /connector): montam a barra lateral. Apresentação, e não permissão.
export type ModuleName = 'Fiscal' | 'Contabil' | 'Inventario';

// GET /info — o que qualquer papel lê para montar a barra lateral e o selo.
export interface InfoResponse {
  environment: string;
  // O adapter de entrada varre e o poll.enabled está ligado (derivado no servidor, ADR-0029).
  automaticIntegration: boolean;
  // O adapter de entrada varre: o selo aparece (verde ou vermelho) só quando é verdadeiro.
  inboundScans: boolean;
  modules: ModuleName[];
}

// GET /connector/automatic — o painel da integração automática (só Admin). Os instantes vêm em ISO (UTC).
export interface AutomaticPanel {
  // O que o coletor registrou; null = ele ainda não passou por este tenant.
  cursor: {
    lastPolledAt: string | null;
    consecutiveFailures: number;
    lastError: string | null;
    // null = o cursor nasceu de uma falha, e a marca ainda não nasceu.
    watermark: string | null;
    notBefore: string | null;
  } | null;
  // O poll.startFrom gravado, só como leitura: sem marca, é dele que a primeira passada começa.
  startFrom: string | null;
}

// POST /connector/test — só isto: se funcionou, o motivo e, com o freio, quando um novo teste é aceito (ISO).
export interface CredentialTestResult {
  worked: boolean;
  reason: string;
  retryAt: string | null;
}

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
  modules: ModuleName[];
}

export interface ConnectorProfileRequest {
  environment: string;
  inboundAdapter: string;
  inboundSettings: string | null;
  outboundAdapter: string;
  outboundSettings: string | null;
  // Ausente mantém os gravados.
  modules?: ModuleName[];
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

// Grupo (empresa/filial/dia da execução/período/tipo/modelo/modo) com contagens — a linha principal do dashboard. O dia é o
// da execução que trouxe as notas, e não a data fiscal delas (change erp-company-directory-and-card-filters, D15).
export interface DocumentGroup {
  companyCode: string;
  branchCode: string;
  executedOn: string; // aaaa-mm-dd, em Brasília: o dia da integração, ou o da busca do coletor
  periodStart: string | null; // o período integrado (aaaa-mm-dd); nulo na automática
  periodEnd: string | null;
  type: string;
  model: string | null; // o modelo do documento (55, SE…); nulo só em registro antigo
  trigger: string; // modo da integração: Automatic | Manual | ScheduledDaily | ScheduledOnce
  total: number;
  finalizadas: number;
  emProcessamento: number;
  comErro: number;
}

// As contagens dos cards de um modelo num período (GET /groups/totals): as mesmas faixas do grupo.
export interface ModelTotals {
  model: string | null;
  total: number;
  finalizadas: number;
  emProcessamento: number;
  comErro: number;
}
