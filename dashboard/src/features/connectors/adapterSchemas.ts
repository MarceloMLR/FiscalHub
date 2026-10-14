import type { ModuleName } from '../../types';

// Schema dos campos de cada adapter. No modelo de produção, isto viria do backend (cada adapter
// declara o próprio schema); aqui fica no front pra simplificar a demo. Segredos são CAMPOS DE ESCRITA
// (ADR-0027): o valor digitado vai para o servidor, que o grava no cofre e guarda só a referência no
// perfil. A leitura nunca devolve o valor — a tela mostra só se está configurado e quando.

export interface AdapterField {
  // Caminho no JSON das settings; ponto para objeto aninhado (ex.: 'auth.clientId').
  key: string;
  label: string;
  placeholder?: string;
  // O exemplo que depende do ambiente da instância (o /info), no lugar do placeholder: o de um ambiente nunca aparece
  // no outro, e sem o /info não aparece nenhum (fieldPlaceholder).
  placeholderByEnvironment?: Record<InstanceEnvironment, string>;
  secret?: boolean; // campo de escrita: senha, sem preenchimento, enviado só quando digitado
  // Obrigatório: vazio, a tela marca a pendência com este texto, que diz o efeito. O segredo tem a marca dele.
  required?: string;
  // Ocupa a linha inteira: URLs e GUIDs, que cortados no meio não se leem.
  wide?: boolean;
}

export type InstanceEnvironment = 'Sandbox' | 'Production';

// As URLs da Avalara por ambiente, escritas por inteiro: a de produção não se calcula tirando o ".sandbox" da outra.
const AVALARA_TOKEN_URL: Record<InstanceEnvironment, string> = {
  Sandbox: 'https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token',
  Production: 'https://api-gateway.avalarabrasil.com.br/oauth/token',
};
const AVALARA_BASE_URL: Record<InstanceEnvironment, string> = {
  Sandbox: 'https://api-gateway.sandbox.avalarabrasil.com.br/',
  Production: 'https://api-gateway.avalarabrasil.com.br/',
};

// O exemplo do campo na instância. O que depende do ambiente segue o do /info: numa instância Sandbox, uma URL de produção
// como exemplo é a que um Admin apressado copia, e autentica contra produção. Antes de o /info responder, ou com um
// ambiente que ele não conhece, não há exemplo — a tela não presume ambiente (D6), como o selo.
export function fieldPlaceholder(field: AdapterField, environment: string | undefined): string | undefined {
  if (!field.placeholderByEnvironment) {
    return field.placeholder;
  }
  return environment === 'Sandbox' || environment === 'Production' ? field.placeholderByEnvironment[environment] : undefined;
}

// Adapters de entrada (ERP). Os campos que a tela não mostra (companies, e da seção poll tudo menos o enabled) são
// preservados ao salvar.
export const INBOUND_ADAPTERS: Record<string, AdapterField[]> = {
  Dynamics365: [
    { key: 'url', label: 'URL do ambiente F&O', placeholder: 'https://empresa.operations.dynamics.com', wide: true },
    { key: 'auth.tenantId', label: 'Tenant do Entra ID', placeholder: '00000000-0000-0000-0000-000000000000', wide: true },
    { key: 'auth.clientId', label: 'Client ID', placeholder: '00000000-0000-0000-0000-000000000000', wide: true },
    { key: 'auth.clientSecret', label: 'Client Secret', secret: true },
  ],
  iScala: [
    { key: 'host', label: 'Host', placeholder: 'iscala.cliente.local' },
    { key: 'company', label: 'Empresa (código)', placeholder: 'B01' },
    { key: 'user', label: 'Usuário', placeholder: 'integracao' },
    { key: 'password', label: 'Senha', secret: true },
  ],
};

// Adapters de entrada que varrem a origem: só eles têm o interruptor "Integração automática", que grava o poll.enabled
// das settings. No backend, varrer é ter um feed de mudanças (IDocumentChangeFeed) registrado para a origem; esta marca
// é o espelho dele na tela, até os schemas virem do backend (ADR-0029).
export const SCANNING_INBOUND_ADAPTERS: ReadonlySet<string> = new Set(['Dynamics365']);

// A conta de plataforma (change platform-account-and-connectors): a credencial é uma só para todos os conectores da
// conta, e mora nela, em campos tipados — iguais para qualquer plataforma. O endpoint de token é obrigatório para enviar e
// para testar, e o hub não o deriva da URL base de nenhum conector (D11). O ambiente é da instância (D6).
// O endpoint de token é o campo que a migração deixa vazio em todo tenant (D8): sem a marca, quem migra cai na tela com o
// conector parado e sem nada dizendo por quê.
export const ACCOUNT_FIELDS: AdapterField[] = [
  { key: 'clientId', label: 'Client ID', required: 'Sem o Client ID, o conector não despacha', wide: true },
  { key: 'clientSecret', label: 'Client Secret', secret: true },
  {
    key: 'tokenUrl',
    label: 'Endpoint de token',
    placeholderByEnvironment: AVALARA_TOKEN_URL,
    required: 'Sem o endpoint de token, o conector não despacha',
    wide: true,
  },
];

// O conector de cada plataforma, pelo adapter, que nesta tela tem o nome da plataforma: as URLs base por versão. A chave
// da versão é rótulo, e nunca entra na URL (D4). Os campos que a tela não mostra (establishments…) são preservados ao
// salvar. Só as plataformas que o servidor declara (D17): a gravação recusa as outras, e uma opção que sempre falha não
// fica na tela.
export const CONNECTOR_ADAPTERS: Record<string, AdapterField[]> = {
  Avalara: [
    { key: 'baseUrls.v2', label: 'URL base (v2)', placeholderByEnvironment: AVALARA_BASE_URL, wide: true },
    { key: 'baseUrls.v1', label: 'URL base (v1)', placeholderByEnvironment: AVALARA_BASE_URL, wide: true },
  ],
};

// O que cada adapter de saída atende: os módulos, as integrações de cada um e quais aceitam lote. É o espelho da declaração
// do servidor (change platform-account-and-connectors, 5.1), até os schemas virem do backend; a gravação confere contra a
// de lá, e recusa o que ela não declara.
export interface DeclaredIntegration {
  key: string;
  label: string;
  acceptsBatch: boolean;
}

export interface DeclaredModule {
  module: ModuleName;
  label: string;
  integrations: DeclaredIntegration[];
}

export const CONNECTOR_DECLARATIONS: Record<string, DeclaredModule[]> = {
  Avalara: [
    { module: 'Fiscal', label: 'Fiscal', integrations: [{ key: 'notas-fiscais', label: 'Notas fiscais', acceptsBatch: false }] },
    {
      module: 'Contabil',
      label: 'Contábil',
      integrations: [
        { key: 'plano-de-contas', label: 'Plano de contas', acceptsBatch: true },
        { key: 'lancamentos', label: 'Lançamentos', acceptsBatch: true },
        { key: 'saldos', label: 'Saldos', acceptsBatch: true },
      ],
    },
    {
      module: 'Inventario',
      label: 'Inventário',
      integrations: [
        { key: 'k200', label: 'Bloco K — K200', acceptsBatch: true },
        { key: 'k210-k215', label: 'Bloco K — K210/K215', acceptsBatch: true },
        { key: 'k230', label: 'Bloco K — K230', acceptsBatch: true },
      ],
    },
  ],
};

// A integração declarada pela chave, e o módulo dela.
export function declaredIntegration(adapter: string, key: string): (DeclaredIntegration & { module: ModuleName }) | undefined {
  for (const m of CONNECTOR_DECLARATIONS[adapter] ?? []) {
    const found = m.integrations.find((i) => i.key === key);
    if (found) {
      return { ...found, module: m.module };
    }
  }
  return undefined;
}

// Adapters com teste de credencial no servidor (IConnectorCredentialTest): só eles ganham o botão "Testar credencial". Na
// saída, o teste é da conta de plataforma.
export const CREDENTIAL_TEST_ADAPTERS = {
  inbound: new Set(['Dynamics365']),
  platform: new Set(['Avalara']),
} as const;
