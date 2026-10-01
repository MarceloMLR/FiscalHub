import type { ConnectorProfile, ConnectorProfileRequest, ModuleName } from '../../types';
import { INBOUND_ADAPTERS, OUTBOUND_ADAPTERS, type AdapterField } from './adapterSchemas';
import { DEFAULT_MODULES } from '../modules/modules';

// A montagem do que a tela de Configurações grava, fora do componente, para ser testada (design D3 e D4 da change
// module-navigation-and-integration-panel). A regra que não pode quebrar: o segredo é campo de escrita pura, e só vai no
// payload quando o usuário o digitou. A máscara (SECRET_MASK) é placeholder do campo, e nunca valor: se virasse valor, o
// próximo salvar gravaria a máscara no cofre e destruiria o segredo.

// Settings como vieram do servidor (sem segredos e sem referências). Os campos que a tela não mostra
// (establishments, companies, poll…) ficam aqui e voltam intactos ao salvar. Da seção poll, a tela só mexe no
// enabled, pelo interruptor "Integração automática".
export type Json = Record<string, unknown>;
// Segredos digitados nesta edição, por caminho (`outbound.sandbox.clientSecret`). Nunca vêm do servidor.
export type Typed = Record<string, string>;

// O placeholder de um segredo configurado. Tamanho fixo, que não conta os caracteres do segredo: 32 bolinhas, mais ou
// menos a largura de um Client ID na tela (pedido da prova manual, 2026-10-01).
export const SECRET_MASK = '•'.repeat(32);

// O formulário, como a tela o guarda.
export interface ConnectorForm {
  environment: string;
  inboundAdapter: string;
  inboundValues: Json;
  outboundAdapter: string;
  outboundRest: Json;
  sandboxValues: Json;
  productionValues: Json;
  modules: ModuleName[];
}

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

// As settings de saída guardam uma seção por ambiente; o resto (fora das seções) volta intacto.
export function splitOutbound(json: string): Pick<ConnectorForm, 'outboundRest' | 'sandboxValues' | 'productionValues'> {
  const { sandbox, production, ...rest } = parseObj(json);
  return { outboundRest: rest, sandboxValues: asObj(sandbox), productionValues: asObj(production) };
}

// O formulário carregado do perfil gravado. Adapter que a tela não conhece cai no padrão dela.
export function formFromProfile(data: ConnectorProfile): ConnectorForm {
  return {
    environment: data.environment,
    inboundAdapter: data.inboundAdapter in INBOUND_ADAPTERS ? data.inboundAdapter : 'Dynamics365',
    inboundValues: parseObj(data.inboundSettings),
    outboundAdapter: data.outboundAdapter in OUTBOUND_ADAPTERS ? data.outboundAdapter : 'Avalara',
    ...splitOutbound(data.outboundSettings),
    modules: data.modules ?? DEFAULT_MODULES,
  };
}

// Aplica os segredos digitados: só o que foi digitado vai; o resto o servidor mantém como estava.
function withTyped(schema: AdapterField[], values: Json, prefix: string, typed: Typed): Json {
  return schema
    .filter((f) => f.secret && typed[prefix + f.key])
    .reduce((acc, f) => setPath(acc, f.key, typed[prefix + f.key]), values);
}

// O PUT /connector do formulário. Nenhum segredo sai daqui sem ter sido digitado.
export function buildConnectorPayload(form: ConnectorForm, typed: Typed): ConnectorProfileRequest {
  const inSchema = INBOUND_ADAPTERS[form.inboundAdapter] ?? [];
  const outSchema = OUTBOUND_ADAPTERS[form.outboundAdapter] ?? [];
  return {
    environment: form.environment,
    inboundAdapter: form.inboundAdapter,
    inboundSettings: JSON.stringify(withTyped(inSchema, form.inboundValues, 'inbound.', typed)),
    outboundAdapter: form.outboundAdapter,
    outboundSettings: JSON.stringify({
      ...form.outboundRest,
      sandbox: withTyped(outSchema, form.sandboxValues, 'outbound.sandbox.', typed),
      production: withTyped(outSchema, form.productionValues, 'outbound.production.', typed),
    }),
    modules: form.modules,
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
