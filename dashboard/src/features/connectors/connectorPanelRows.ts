import type { ModuleName } from '../../types';
import { MODULE_VIEWS } from '../modules/modules';
import { CONNECTOR_DECLARATIONS, type DeclaredModule } from './adapterSchemas';
import { shownConnector, type AccountForm, type ConnectorEntry } from './connectorPayload';

// O que a tela do canvas mostra (change platform-account-and-connectors, grupo 7; ADR-0039), fora do componente para ser
// testado: a lista de conectores da conta e as linhas das abas do painel de um conector. Não muda o dado: o payload sai do
// mesmo montador do grupo 6 (connectorPayload), e as mudanças, das mesmas funções.

// Os conectores da conta mostrada; sem nenhum, o do adapter da plataforma, para começar.
export function connectorList(account: AccountForm): ConnectorEntry[] {
  return account.connectors.length > 0 ? account.connectors : [shownConnector(account)];
}

// O resumo do conector na lista: singular ou plural pelo número, e não "módulo(s)", para caber numa linha da coluna.
export function connectorSummary(entry: ConnectorEntry): string {
  const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
  return `${count(entry.modules.length, 'módulo', 'módulos')} · ${count(entry.integrations.length, 'integração', 'integrações')}`;
}

// O conector que o painel mostra: o escolhido na lista; sem escolha, ou com uma que a conta não tem, o do adapter de mesmo
// nome da plataforma — o mesmo que a tela do grupo 6 mostrava.
export function selectedConnector(account: AccountForm, adapter: string | null): ConnectorEntry {
  const list = connectorList(account);
  return list.find((c) => c.adapter === adapter) ?? list.find((c) => c.adapter === account.platform) ?? list[0];
}

export interface IntegrationRow {
  key: string;
  label: string;
  on: boolean;
}

export interface ModuleRow {
  module: ModuleName;
  label: string;
  declared: boolean;
  on: boolean;
  // Por que a caixa não marca: o conector não atende o módulo (7.2). Nulo para o declarado.
  reason: string | null;
  integrations: IntegrationRow[];
}

// A aba Módulos e integrações: os três módulos do hub, na ordem da barra lateral. O que o adapter não declara aparece
// apagado, dizendo que aquele conector não o atende — apagado com motivo responde a pergunta; ausente faz procurar
// (module-navigation). As integrações são as que o adapter declara para o módulo.
export function moduleRows(entry: ConnectorEntry, declarations: Record<string, DeclaredModule[]> = CONNECTOR_DECLARATIONS): ModuleRow[] {
  const declared = declarations[entry.adapter] ?? [];
  return MODULE_VIEWS.flatMap((view) => {
    if (!view.module) {
      return [];
    }
    const module = view.module;
    const declaration = declared.find((m) => m.module === module);
    return [{
      module,
      label: view.label,
      declared: !!declaration,
      on: !!declaration && entry.modules.includes(module),
      reason: declaration ? null : `O conector ${entry.adapter} não atende o módulo ${view.label}.`,
      integrations: (declaration?.integrations ?? []).map((i) => ({
        key: i.key,
        label: i.label,
        on: entry.integrations.some((x) => x.key === i.key),
      })),
    }];
  });
}

export interface EndpointRow {
  key: string;
  label: string;
  module: ModuleName;
  endpoint: string | null;
  batchSize: string;
  acceptsBatch: boolean;
  // A pendência da integração sem caminho (D5): à espera do host, ou inativa.
  pending: string | null;
}

// A aba Endpoints: as integrações ligadas, na ordem da declaração, com o caminho, o lote onde a API aceita e a pendência.
export function endpointRows(entry: ConnectorEntry, declarations: Record<string, DeclaredModule[]> = CONNECTOR_DECLARATIONS): EndpointRow[] {
  return (declarations[entry.adapter] ?? []).flatMap((m) =>
    m.integrations.flatMap((i) => {
      const current = entry.integrations.find((x) => x.key === i.key);
      if (!current) {
        return [];
      }
      return [{
        key: i.key,
        label: i.label,
        module: m.module,
        endpoint: current.endpoint,
        batchSize: current.batchSize,
        acceptsBatch: i.acceptsBatch,
        pending:
          current.endpoint === null
            ? 'À espera do caminho que o host preenche na subida'
            : current.endpoint.trim() === ''
              ? 'Sem caminho: a integração fica inativa'
              : null,
      }];
    }),
  );
}
