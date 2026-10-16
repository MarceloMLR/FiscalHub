import type {
  ConnectorIntegrationRequest,
  ConnectorProfile,
  ConnectorProfileRequest,
  ModuleName,
  PlatformAccountRequest,
} from '../../types';
import { CONNECTOR_ADAPTERS, CONNECTOR_DECLARATIONS, INBOUND_ADAPTERS, declaredIntegration, type AdapterField } from './adapterSchemas';

// A montagem do que a tela de Configurações grava, fora do componente, para ser testada (design D3 e D4 da change
// module-navigation-and-integration-panel). A regra que não pode quebrar: o segredo é campo de escrita pura, e só vai no
// payload quando o usuário o digitou. A máscara (SECRET_MASK) é placeholder do campo, e nunca valor: se virasse valor, o
// próximo salvar gravaria a máscara no cofre e destruiria o segredo.

// Settings como vieram do servidor (sem segredos e sem referências). Os campos que a tela não mostra
// (establishments, companies, poll…) ficam aqui e voltam intactos ao salvar. Da seção poll, a tela só mexe no
// enabled, pelo interruptor "Integração automática".
export type Json = Record<string, unknown>;
// Segredos digitados nesta edição, por caminho (`platform.Avalara.clientSecret`). Nunca vêm do servidor.
export type Typed = Record<string, string>;

// O placeholder de um segredo configurado. Tamanho fixo, que não conta os caracteres do segredo: 32 bolinhas, mais ou
// menos a largura de um Client ID na tela (pedido da prova manual, 2026-10-01).
export const SECRET_MASK = '•'.repeat(32);

// Uma integração ligada, como a tela a guarda. O caminho nulo é o que espera o host (D10): enquanto a tela não o edita, ele
// não vai no payload, e o servidor o mantém; editado, vai como texto, e vazio é a integração inativa (D5). O lote é texto
// do campo, e vai como número só onde a integração aceita.
export interface IntegrationForm {
  key: string;
  endpoint: string | null;
  batchSize: string;
}

// Um conector da conta, como a tela o guarda: o adapter, as settings do produto, e o que ele liga.
export interface ConnectorEntry {
  adapter: string;
  values: Json;
  modules: ModuleName[];
  integrations: IntegrationForm[];
}

// Uma conta de plataforma, como a tela a guarda (change platform-account-and-connectors): os campos tipados (clientId,
// tokenUrl) e os conectores sob ela. O Client Secret é campo de escrita, e não fica aqui: só no Typed.
export interface AccountForm {
  platform: string;
  values: Json;
  connectors: ConnectorEntry[];
}

// O formulário, como a tela o guarda. O ambiente não é campo: é da instância, e a tela o lê do /info (D6). Os módulos também
// não: são de cada conector (D7). A tela mostra uma conta, a da plataforma escolhida, e o conector do adapter de mesmo nome;
// as outras contas e os outros conectores do perfil ficam em `accounts` e voltam intactos ao salvar.
export interface ConnectorForm {
  inboundAdapter: string;
  inboundValues: Json;
  platform: string;
  accounts: AccountForm[];
  // A chave de envio ao conector de saída (ADR-0041). É do tenant, e não de um conector: fica fora de `accounts`, ao
  // lado do adapter de entrada, como a chave de outro eixo que é.
  outboundDispatch: boolean;
}

// O caminho dos segredos no mapa `secrets` da leitura e no Typed: o da conta e o de cada conector.
export const accountSecretPrefix = (platform: string) => `platform.${platform}.`;
export const connectorSecretPrefix = (adapter: string) => `connector.${adapter}.`;

export function parseObj(json: string): Json {
  try {
    const value = JSON.parse(json || '{}') as unknown;
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as Json) : {};
  } catch {
    return {};
  }
}

export function asObj(value: unknown): Json {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as Json) : {};
}

export function getPath(obj: Json, path: string): string {
  const value = path.split('.').reduce<unknown>((cur, k) => asObj(cur)[k], obj);
  return typeof value === 'string' || typeof value === 'number' ? String(value) : '';
}

export function setPath(obj: Json, path: string, value: string): Json {
  const [head, ...rest] = path.split('.');
  if (rest.length === 0) {
    return { ...obj, [head]: value };
  }
  return { ...obj, [head]: setPath(asObj(obj[head]), rest.join('.'), value) };
}

// O formulário carregado do perfil gravado. Adapter de entrada que a tela não conhece cai no padrão dela; a conta
// mostrada é a primeira do perfil, se a tela conhece a plataforma, e senão a Avalara.
export function formFromProfile(data: ConnectorProfile): ConnectorForm {
  const accounts = (data.accounts ?? []).map<AccountForm>((a) => ({
    platform: a.platform,
    values: {
      ...(a.clientId ? { clientId: a.clientId } : {}),
      ...(a.tokenUrl ? { tokenUrl: a.tokenUrl } : {}),
    },
    connectors: a.connectors.map((c) => ({
      adapter: c.adapter,
      values: parseObj(c.settings),
      modules: c.modules ?? [],
      integrations: (c.integrations ?? []).map((i) => ({
        key: i.key,
        endpoint: i.endpoint,
        batchSize: i.batchSize === undefined ? '' : String(i.batchSize),
      })),
    })),
  }));
  const first = accounts[0]?.platform;
  return {
    inboundAdapter: data.inboundAdapter in INBOUND_ADAPTERS ? data.inboundAdapter : 'Dynamics365',
    inboundValues: parseObj(data.inboundSettings),
    platform: first && first in CONNECTOR_ADAPTERS ? first : 'Avalara',
    accounts,
    // Perfil de antes da coluna, ou resposta sem o campo: ligada, que é o comportamento de sempre.
    outboundDispatch: data.outboundDispatch ?? true,
  };
}

// A conta que a tela mostra: a gravada da plataforma escolhida, ou uma em branco.
export function shownAccount(form: ConnectorForm): AccountForm {
  return form.accounts.find((a) => a.platform === form.platform) ?? { platform: form.platform, values: {}, connectors: [] };
}

// O conector que a tela mostra na conta: o do adapter de mesmo nome da plataforma, ou um em branco.
export function shownConnector(account: AccountForm): ConnectorEntry {
  return (
    account.connectors.find((c) => c.adapter === account.platform) ?? { adapter: account.platform, values: {}, modules: [], integrations: [] }
  );
}

// Liga ou desliga um módulo no conector. Desligar tira as integrações dele: uma integração só fica ligada com o módulo
// (module-navigation). A ordem é a da declaração.
export function toggleModule(entry: ConnectorEntry, module: ModuleName, on: boolean): ConnectorEntry {
  const declared = (CONNECTOR_DECLARATIONS[entry.adapter] ?? []).map((m) => m.module);
  const modules = declared.filter((m) => (m === module ? on : entry.modules.includes(m)));
  const integrations = on
    ? entry.integrations
    : entry.integrations.filter((i) => declaredIntegration(entry.adapter, i.key)?.module !== module);
  return { ...entry, modules, integrations };
}

// Liga ou desliga uma integração. Ligada, ela entra sem caminho — inativa até ganhar um —; com o módulo dela desligado, não
// liga.
export function toggleIntegration(entry: ConnectorEntry, key: string, on: boolean): ConnectorEntry {
  if (!on) {
    return { ...entry, integrations: entry.integrations.filter((i) => i.key !== key) };
  }
  const declared = declaredIntegration(entry.adapter, key);
  if (!declared || !entry.modules.includes(declared.module) || entry.integrations.some((i) => i.key === key)) {
    return entry;
  }
  return { ...entry, integrations: [...entry.integrations, { key, endpoint: '', batchSize: '' }] };
}

// Muda o caminho ou o lote de uma integração ligada.
export function setIntegration(entry: ConnectorEntry, key: string, patch: Partial<Omit<IntegrationForm, 'key'>>): ConnectorEntry {
  return { ...entry, integrations: entry.integrations.map((i) => (i.key === key ? { ...i, ...patch } : i)) };
}

// A pendência de um campo obrigatório, com o efeito: vazio, a tela o marca, como marca o segredo não configurado e a
// integração sem caminho. O segredo tem a marca dele, e não entra aqui.
export function fieldPending(field: AdapterField, values: Json): string | null {
  return field.required && !field.secret && getPath(values, field.key).trim() === '' ? field.required : null;
}

// O teste da conta usa a credencial gravada (D9): sem uma parte dela, não há o que testar, e a tela diz o que falta em vez
// de chamar o servidor — que responderia só "Credenciais ou ambiente inválidos", com o detalhe no log do host.
export function platformTestBlocker(data: ConnectorProfile, platform: string): string | null {
  const stored = (data.accounts ?? []).find((a) => a.platform === platform);
  if (!stored) {
    return 'Falta a conta de plataforma. Grave-a para testar.';
  }
  const missing = [
    ...(stored.clientId?.trim() ? [] : ['o Client ID']),
    ...(data.secrets?.[accountSecretPrefix(platform) + 'clientSecret']?.configured ? [] : ['o Client Secret']),
    ...(stored.tokenUrl?.trim() ? [] : ['o endpoint de token']),
  ];
  if (missing.length === 0) {
    return null;
  }
  if (missing.length === 1) {
    return `Falta ${missing[0]}. Grave-o para testar.`;
  }
  return `Faltam ${missing.slice(0, -1).join(', ')} e ${missing[missing.length - 1]}. Grave-os para testar.`;
}

// As integrações ligadas sem caminho: inativas, e a pendência que a tela mostra (D5).
export function pendingIntegrations(entry: ConnectorEntry): string[] {
  return entry.integrations.filter((i) => !i.endpoint?.trim()).map((i) => i.key);
}

// A integração na gravação: o caminho nulo não vai (o servidor mantém o que espera o host); o lote só onde a integração
// aceita, e só quando é um número inteiro positivo — o resto o servidor recusaria.
function integrationRequest(adapter: string, integration: IntegrationForm): ConnectorIntegrationRequest {
  const acceptsBatch = declaredIntegration(adapter, integration.key)?.acceptsBatch ?? false;
  const size = Number(integration.batchSize);
  return {
    key: integration.key,
    ...(integration.endpoint === null ? {} : { endpoint: integration.endpoint.trim() }),
    ...(acceptsBatch && integration.batchSize.trim() !== '' && Number.isInteger(size) ? { batchSize: size } : {}),
  };
}

// O formulário com a conta trocada — ou acrescentada, na primeira edição de uma plataforma que ainda não tem conta.
export function withAccount(form: ConnectorForm, account: AccountForm): ConnectorForm {
  const exists = form.accounts.some((a) => a.platform === account.platform);
  return {
    ...form,
    accounts: exists ? form.accounts.map((a) => (a.platform === account.platform ? account : a)) : [...form.accounts, account],
  };
}

// A conta com o conector trocado, ou acrescentado.
export function withConnector(account: AccountForm, connector: ConnectorEntry): AccountForm {
  const exists = account.connectors.some((c) => c.adapter === connector.adapter);
  return {
    ...account,
    connectors: exists
      ? account.connectors.map((c) => (c.adapter === connector.adapter ? connector : c))
      : [...account.connectors, connector],
  };
}

// Algum segredo digitado sob o prefixo?
function typedUnder(typed: Typed, prefix: string): boolean {
  return Object.entries(typed).some(([k, v]) => k.startsWith(prefix) && v !== '');
}

// Aplica os segredos digitados: só o que foi digitado vai; o resto o servidor mantém como estava.
function withTyped(schema: AdapterField[], values: Json, prefix: string, typed: Typed): Json {
  return schema
    .filter((f) => f.secret && typed[prefix + f.key])
    .reduce((acc, f) => setPath(acc, f.key, typed[prefix + f.key]), values);
}

// A conta na gravação. O Client Secret só vai quando digitado; o campo vazio vai como ausente (null).
function accountRequest(account: AccountForm, typed: Typed): PlatformAccountRequest {
  const secret = typed[accountSecretPrefix(account.platform) + 'clientSecret'];
  // Um segredo digitado para o conector mostrado, que a conta ainda não tem, cria o conector.
  const connectors =
    account.connectors.some((c) => c.adapter === account.platform) || !typedUnder(typed, connectorSecretPrefix(account.platform))
      ? account.connectors
      : [...account.connectors, shownConnector(account)];
  return {
    platform: account.platform,
    clientId: getPath(account.values, 'clientId') || null,
    ...(secret ? { clientSecret: secret } : {}),
    tokenUrl: getPath(account.values, 'tokenUrl') || null,
    connectors: connectors.map((c) => ({
      adapter: c.adapter,
      settings: JSON.stringify(withTyped(CONNECTOR_ADAPTERS[c.adapter] ?? [], c.values, connectorSecretPrefix(c.adapter), typed)),
      modules: c.modules,
      integrations: c.integrations.map((i) => integrationRequest(c.adapter, i)),
    })),
  };
}

// O PUT /connector do formulário. Nenhum segredo sai daqui sem ter sido digitado. Olhar outra plataforma não cria conta:
// a conta nova entra na primeira edição (withAccount) ou no primeiro segredo digitado para ela.
export function buildConnectorPayload(form: ConnectorForm, typed: Typed): ConnectorProfileRequest {
  const inSchema = INBOUND_ADAPTERS[form.inboundAdapter] ?? [];
  const shown = shownAccount(form);
  const accounts =
    form.accounts.some((a) => a.platform === shown.platform) ||
    !(typedUnder(typed, accountSecretPrefix(shown.platform)) || typedUnder(typed, connectorSecretPrefix(shown.platform)))
      ? form.accounts
      : [...form.accounts, shown];
  return {
    inboundAdapter: form.inboundAdapter,
    inboundSettings: JSON.stringify(withTyped(inSchema, form.inboundValues, 'inbound.', typed)),
    accounts: accounts.map((a) => accountRequest(a, typed)),
    outboundDispatch: form.outboundDispatch,
  };
}

// Há edição pendente? O teste de credencial usa a credencial gravada: com uma edição na tela, ele responderia sobre
// outra credencial, e o botão pede para salvar antes (D9). Pendente é um segredo digitado, ou um formulário que já não
// monta o mesmo payload do perfil carregado.
export function hasPendingEdit(form: ConnectorForm, typed: Typed, loaded: ConnectorProfile): boolean {
  if (Object.values(typed).some((v) => v !== '')) {
    return true;
  }
  return JSON.stringify(buildConnectorPayload(form, {})) !== JSON.stringify(buildConnectorPayload(formFromProfile(loaded), {}));
}
