import { describe, expect, it } from 'vitest';
import type { ConnectorProfile } from '../../types';
import type { DeclaredModule } from './adapterSchemas';
import { buildConnectorPayload, formFromProfile, setIntegration, shownAccount, shownConnector, toggleIntegration, toggleModule, withAccount, withConnector, type ConnectorEntry } from './connectorPayload';
import { connectorList, connectorSummary, endpointRows, moduleRows, selectedConnector } from './connectorPanelRows';

// A tela do canvas (change platform-account-and-connectors, grupo 7; ADR-0039): a lista de conectores da conta e o painel
// de um conector, com as abas Conexão, Módulos e integrações, e Endpoints. Ela não muda o dado: monta o mesmo payload que a
// tela do grupo 6 (7.3), e o segredo continua campo de escrita pura.

const profile: ConnectorProfile = {
  tenantId: 'tenant-a',
  inboundAdapter: 'Dynamics365',
  inboundSettings: '{}',
  supportAdapter: null,
  supportSettings: '{}',
  secrets: { 'platform.Avalara.clientSecret': { configured: true, updatedOn: '2026-09-28T17:05:00Z' } },
  modules: ['Fiscal', 'Contabil'],
  outboundDispatch: true,
  accounts: [
    {
      platform: 'Avalara',
      clientId: 'abc',
      tokenUrl: 'https://sandbox.example/oauth/token',
      connectors: [
        {
          adapter: 'Avalara',
          settings: JSON.stringify({ baseUrls: { v2: 'https://sandbox.example/' } }),
          modules: ['Fiscal', 'Contabil'],
          integrations: [
            { key: 'notas-fiscais', module: 'Fiscal', endpoint: 'taxcompliance/v2/fiscal/dfe', withoutPath: false },
            { key: 'plano-de-contas', module: 'Contabil', endpoint: '', batchSize: 100, withoutPath: true },
          ],
        },
      ],
    },
  ],
};

describe('a lista de conectores (7.1)', () => {
  it('lista os conectores da conta mostrada', () => {
    expect(connectorList(shownAccount(formFromProfile(profile))).map((c) => c.adapter)).toEqual(['Avalara']);
  });

  it('a conta sem conector oferece o do adapter da plataforma, para começar', () => {
    const empty = { ...profile, accounts: [{ ...profile.accounts[0], connectors: [] }] };

    expect(connectorList(shownAccount(formFromProfile(empty))).map((c) => c.adapter)).toEqual(['Avalara']);
  });
});

describe('o resumo do conector na lista (7.1)', () => {
  // Singular ou plural pelo número, e não "módulo(s)": o resumo cabe numa linha da coluna estreita.
  const entry = (modules: number, integrations: number): ConnectorEntry => ({
    adapter: 'Avalara',
    values: {},
    modules: (['Fiscal', 'Contabil', 'Inventario'] as const).slice(0, modules),
    integrations: ['notas-fiscais', 'plano-de-contas', 'lancamentos', 'saldos', 'k200', 'k210-k215', 'k230']
      .slice(0, integrations)
      .map((key) => ({ key, endpoint: '', batchSize: '' })),
  });

  it.each([
    [0, 0, '0 módulos · 0 integrações'],
    [1, 0, '1 módulo · 0 integrações'],
    [0, 1, '0 módulos · 1 integração'],
    [1, 1, '1 módulo · 1 integração'],
    [3, 1, '3 módulos · 1 integração'],
    [1, 7, '1 módulo · 7 integrações'],
    [3, 7, '3 módulos · 7 integrações'],
  ])('%i e %i: "%s"', (modules, integrations, expected) => {
    expect(connectorSummary(entry(modules, integrations))).toBe(expected);
  });
});

describe('o conector escolhido na lista (7.1)', () => {
  const twoConnectors = {
    ...profile,
    accounts: [{
      ...profile.accounts[0],
      connectors: [{ ...profile.accounts[0].connectors[0], adapter: 'Outro' }, profile.accounts[0].connectors[0]],
    }],
  };

  it('sem escolha, é o do adapter de mesmo nome da plataforma, o mesmo que a tela do grupo 6 mostrava', () => {
    const account = shownAccount(formFromProfile(twoConnectors));

    expect(selectedConnector(account, null)).toEqual(shownConnector(account));
  });

  it('a escolha mostra o conector escolhido; uma escolha que a conta não tem volta ao do adapter da plataforma', () => {
    const account = shownAccount(formFromProfile(twoConnectors));

    expect(selectedConnector(account, 'Outro').adapter).toBe('Outro');
    expect(selectedConnector(account, 'Sumiu').adapter).toBe('Avalara');
  });
});

describe('a aba Módulos e integrações (7.1, 7.2)', () => {
  it('mostra os três módulos do hub, na ordem da barra lateral, com as integrações do ligado', () => {
    const connector = connectorList(shownAccount(formFromProfile(profile)))[0];

    const rows = moduleRows(connector);

    expect(rows.map((r) => `${r.label}:${r.on ? 'ligado' : 'desligado'}`)).toEqual(['Fiscal:ligado', 'Contábil:ligado', 'Inventário:desligado']);
    expect(rows[1].integrations.map((i) => `${i.label}:${i.on ? 'ligada' : 'desligada'}`)).toEqual([
      'Plano de contas:ligada',
      'Lançamentos:desligada',
      'Saldos:desligada',
    ]);
  });

  it('o módulo que o adapter não declara aparece apagado, dizendo que o conector não o atende, e não marca', () => {
    // Um adapter que só atende o Fiscal, como o AvaTax atenderá: o Contábil e o Inventário aparecem, apagados, com o motivo.
    const onlyFiscal: Record<string, DeclaredModule[]> = {
      SoFiscal: [{ module: 'Fiscal', label: 'Fiscal', integrations: [{ key: 'emissao', label: 'Emissão', acceptsBatch: false }] }],
    };
    const connector = { adapter: 'SoFiscal', values: {}, modules: [], integrations: [] };

    const rows = moduleRows(connector, onlyFiscal);

    expect(rows.map((r) => [r.label, r.declared, r.reason])).toEqual([
      ['Fiscal', true, null],
      ['Contábil', false, 'O conector SoFiscal não atende o módulo Contábil.'],
      ['Inventário', false, 'O conector SoFiscal não atende o módulo Inventário.'],
    ]);
    expect(toggleModule(connector, 'Contabil', true).modules).toEqual([]);   // a caixa não marca
  });
});

describe('a aba Endpoints (7.1)', () => {
  it('traz as integrações ligadas, com o caminho, o lote onde a API aceita e a pendência', () => {
    const connector = connectorList(shownAccount(formFromProfile(profile)))[0];

    expect(endpointRows(connector)).toEqual([
      { key: 'notas-fiscais', label: 'Notas fiscais', module: 'Fiscal', endpoint: 'taxcompliance/v2/fiscal/dfe', batchSize: '', acceptsBatch: false, pending: null },
      { key: 'plano-de-contas', label: 'Plano de contas', module: 'Contabil', endpoint: '', batchSize: '100', acceptsBatch: true, pending: 'Sem caminho: a integração fica inativa' },
    ]);
  });

  it('o caminho que espera o host tem a pendência dele', () => {
    const waiting = {
      ...profile,
      accounts: [{
        ...profile.accounts[0],
        connectors: [{ ...profile.accounts[0].connectors[0], integrations: [{ key: 'notas-fiscais', module: 'Fiscal' as const, endpoint: null, withoutPath: true }] }],
      }],
    };
    const connector = connectorList(shownAccount(formFromProfile(waiting)))[0];

    expect(endpointRows(connector)[0].pending).toBe('À espera do caminho que o host preenche na subida');
  });
});

describe('o painel monta o mesmo payload da tela do grupo 6 (7.3)', () => {
  it('ligar um módulo, uma integração e dar o caminho pelo painel dá o payload que o grupo 6 dava', () => {
    const form = formFromProfile(profile);
    const account = shownAccount(form);
    let connector = connectorList(account)[0];
    connector = toggleModule(connector, 'Inventario', true);
    connector = toggleIntegration(connector, 'k200', true);
    connector = setIntegration(connector, 'k200', { endpoint: 'bloco-k/k200', batchSize: '100' });

    const payload = buildConnectorPayload(withAccount(form, withConnector(account, connector)), {});

    expect(payload.accounts?.[0].connectors[0]).toMatchObject({
      adapter: 'Avalara',
      modules: ['Fiscal', 'Contabil', 'Inventario'],
      integrations: [
        { key: 'notas-fiscais', endpoint: 'taxcompliance/v2/fiscal/dfe' },
        { key: 'plano-de-contas', endpoint: '', batchSize: 100 },
        { key: 'k200', endpoint: 'bloco-k/k200', batchSize: 100 },
      ],
    });
    expect(payload).not.toHaveProperty('modules');
  });

  it('as mesmas mudanças, pelo painel e pela tela do grupo 6, dão o mesmo payload', () => {
    const form = formFromProfile(profile);
    const account = shownAccount(form);
    const edit = (c: ConnectorEntry) =>
      setIntegration(toggleIntegration(toggleModule(c, 'Inventario', true), 'k230', true), 'k230', { endpoint: 'bloco-k/k230', batchSize: '50' });

    const byPanel = buildConnectorPayload(withAccount(form, withConnector(account, edit(selectedConnector(account, null)))), {});
    const byGroup6 = buildConnectorPayload(withAccount(form, withConnector(account, edit(shownConnector(account)))), {});

    expect(byPanel).toEqual(byGroup6);
  });

  it('salvar pelo painel sem digitar continua não levando o segredo', () => {
    const form = formFromProfile(profile);
    const account = shownAccount(form);

    const payload = buildConnectorPayload(withAccount(form, withConnector(account, connectorList(account)[0])), {});

    expect(JSON.stringify(payload)).not.toContain('clientSecret');
  });
});
