import { describe, expect, it } from 'vitest';
import type { ConnectorProfile } from '../../types';
import { ACCOUNT_FIELDS } from './adapterSchemas';
import {
  SECRET_MASK,
  buildConnectorPayload,
  fieldPending,
  formFromProfile,
  hasPendingEdit,
  platformTestBlocker,
  pendingIntegrations,
  setIntegration,
  setPath,
  shownAccount,
  shownConnector,
  toggleIntegration,
  toggleModule,
  withAccount,
  withConnector,
  type ConnectorForm,
  type ConnectorEntry,
} from './connectorPayload';

// O que a tela de Configurações envia (design D3): o segredo é campo de escrita pura, e o salvar sem digitar nada não
// leva o campo — nem vazio, nem com a máscara. É o lado da tela da prova; o do servidor está no
// ConnectorProfileServiceTests (o campo ausente preserva o segredo, e a máscara como valor é recusada).
// O ambiente é da instância (change platform-account-and-connectors, D6): a tela não manda ambiente. A saída é a conta de
// plataforma — clientId, Client Secret e endpoint de token — e os conectores sob ela, com as URLs base por versão (4.10),
// os módulos e as integrações (6.13). A lista de módulos do tenant não existe mais (D7).

// Um perfil como o GET /connector devolve: sem segredo e sem referência, e os dois segredos configurados. O conector tem o
// Fiscal, com a integração de notas à espera do host (o caminho nulo da migração), e o Contábil, com o plano de contas
// inativo e com lote.
const profile: ConnectorProfile = {
  tenantId: 'tenant-a',
  inboundAdapter: 'Dynamics365',
  inboundSettings: JSON.stringify({
    url: 'https://fiscosysdev.operations.dynamics.com',
    auth: { tenantId: 'entra', clientId: 'app' },
    poll: { enabled: true, startFrom: '2015-01-01T00:00:00Z' },
  }),
  supportAdapter: null,
  supportSettings: '{}',
  secrets: {
    'inbound.auth.clientSecret': { configured: true, updatedOn: '2026-09-28T17:05:00Z' },
    'platform.Avalara.clientSecret': { configured: true, updatedOn: '2026-09-28T17:05:00Z' },
  },
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
          settings: JSON.stringify({
            baseUrls: { v2: 'https://sandbox.example/' },
            establishments: { '44278225000180': { codigoEmpresa: 'E', codigoContribuinte: 'C' } },
          }),
          modules: ['Fiscal', 'Contabil'],
          integrations: [
            { key: 'notas-fiscais', module: 'Fiscal', endpoint: null, withoutPath: true },
            { key: 'plano-de-contas', module: 'Contabil', endpoint: '', batchSize: 100, withoutPath: true },
          ],
        },
      ],
    },
  ],
};

const MASK_CHARACTERS = ['*', '•', '●', '∗'];

function sent(payload: ReturnType<typeof buildConnectorPayload>) {
  return JSON.stringify(payload);
}

function account(payload: ReturnType<typeof buildConnectorPayload>, platform = 'Avalara') {
  const found = (payload.accounts ?? []).find((a) => a.platform === platform);
  if (!found) {
    throw new Error(`sem a conta ${platform}`);
  }
  return found;
}

function connector(payload: ReturnType<typeof buildConnectorPayload>, adapter = 'Avalara') {
  const found = account(payload, adapter).connectors.find((c) => c.adapter === adapter);
  if (!found) {
    throw new Error(`sem o conector ${adapter}`);
  }
  return found;
}

function connectorSettings(payload: ReturnType<typeof buildConnectorPayload>, adapter = 'Avalara') {
  return JSON.parse(connector(payload, adapter).settings ?? '{}') as Record<string, unknown>;
}

// O formulário com o conector mostrado trocado pelo que a função devolve.
function editConnector(form: ConnectorForm, edit: (c: ConnectorEntry) => ConnectorEntry): ConnectorForm {
  const shown = shownAccount(form);
  return withAccount(form, withConnector(shown, edit(shownConnector(shown))));
}

describe('o payload do perfil', () => {
  it('manda a conta e o conector, e não tem outboundAdapter, outboundSettings nem ambiente', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {});

    expect(payload).not.toHaveProperty('outboundAdapter');
    expect(payload).not.toHaveProperty('outboundSettings');
    expect(payload).not.toHaveProperty('environment');
    expect(account(payload)).toMatchObject({
      platform: 'Avalara',
      clientId: 'abc',
      tokenUrl: 'https://sandbox.example/oauth/token',
    });
    expect(connectorSettings(payload).baseUrls).toEqual({ v2: 'https://sandbox.example/' });
  });

  it('salvar sem digitar não leva nenhum segredo nem a máscara, na entrada e na conta', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {});

    expect(payload.inboundSettings).not.toContain('clientSecret');
    expect(account(payload)).not.toHaveProperty('clientSecret');
    expect(sent(payload)).not.toContain('clientSecret');
    expect(sent(payload)).not.toContain(SECRET_MASK);
    for (const c of MASK_CHARACTERS) {
      expect(sent(payload)).not.toContain(c);
    }
  });

  it('mudar só a URL base da v2 também não leva o segredo', () => {
    const form = editConnector(formFromProfile(profile), (c) => ({ ...c, values: setPath(c.values, 'baseUrls.v2', 'https://outra.example/') }));

    const payload = buildConnectorPayload(form, {});

    expect(connectorSettings(payload).baseUrls).toEqual({ v2: 'https://outra.example/' });
    expect(sent(payload)).not.toContain('clientSecret');
  });

  it('o segredo da conta digitado vai na conta, e só ele', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), { 'platform.Avalara.clientSecret': 'n0v0' });

    expect(account(payload).clientSecret).toBe('n0v0');
    expect(payload.inboundSettings).not.toContain('clientSecret');
    expect(connectorSettings(payload)).not.toHaveProperty('clientSecret');   // a credencial é da conta, e não do conector
  });

  it('o campo digitado e apagado conta como não digitado', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {
      'inbound.auth.clientSecret': '',
      'platform.Avalara.clientSecret': '',
    });

    expect(sent(payload)).not.toContain('clientSecret');
  });

  it('o endpoint de token é campo da conta, e vazio vai como ausente', () => {
    let form = formFromProfile(profile);
    form = withAccount(form, { ...shownAccount(form), values: setPath(shownAccount(form).values, 'tokenUrl', '') });

    const payload = buildConnectorPayload(form, {});

    expect(account(payload).tokenUrl).toBeNull();
    expect(connectorSettings(payload)).not.toHaveProperty('tokenUrl');
  });

  it('as settings que a tela não mostra voltam intactas', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {});

    expect(JSON.parse(payload.inboundSettings ?? '{}').poll).toEqual({ enabled: true, startFrom: '2015-01-01T00:00:00Z' });
    expect(connectorSettings(payload).establishments).toEqual({
      '44278225000180': { codigoEmpresa: 'E', codigoContribuinte: 'C' },
    });
  });

  it('as outras contas e os outros conectores do perfil voltam intactos', () => {
    const withMore: ConnectorProfile = {
      ...profile,
      accounts: [
        {
          ...profile.accounts[0],
          connectors: [
            ...profile.accounts[0].connectors,
            { adapter: 'Outro', settings: JSON.stringify({ baseUrls: { v1: 'https://outro/' } }), modules: [], integrations: [] },
          ],
        },
        { platform: 'PlataformaFicticia', clientId: 'm', tokenUrl: null, connectors: [] },
      ],
    };

    const payload = buildConnectorPayload(formFromProfile(withMore), {});

    expect(payload.accounts?.map((a) => a.platform)).toEqual(['Avalara', 'PlataformaFicticia']);
    expect(connectorSettings(payload, 'Avalara')).toHaveProperty('establishments');
    expect(account(payload).connectors.map((c) => c.adapter)).toEqual(['Avalara', 'Outro']);
    expect(account(payload, 'PlataformaFicticia')).toMatchObject({ clientId: 'm', tokenUrl: null, connectors: [] });
  });

  it('olhar outra plataforma sem mexer não cria conta', () => {
    const form = { ...formFromProfile(profile), platform: 'PlataformaFicticia' };

    const payload = buildConnectorPayload(form, {});

    expect(payload.accounts?.map((a) => a.platform)).toEqual(['Avalara']);
  });

  it('o segredo digitado numa plataforma ainda sem conta cria a conta com ele', () => {
    const form = { ...formFromProfile({ ...profile, accounts: [] }), platform: 'Avalara' };

    const payload = buildConnectorPayload(form, { 'platform.Avalara.clientSecret': 'n0v0' });

    expect(account(payload)).toMatchObject({ platform: 'Avalara', clientSecret: 'n0v0', clientId: null, tokenUrl: null });
  });

  it('um perfil sem conta não manda conta nenhuma', () => {
    const payload = buildConnectorPayload(formFromProfile({ ...profile, accounts: [] }), {});

    expect(payload.accounts).toEqual([]);
  });
});

describe('a chave de envio ao conector de saída (capture-without-dispatch, 6.2)', () => {
  it('vai no corpo com o valor do formulário', () => {
    const ligada = buildConnectorPayload(formFromProfile(profile), {});
    const desligada = buildConnectorPayload({ ...formFromProfile(profile), outboundDispatch: false }, {});

    expect(ligada.outboundDispatch).toBe(true);
    expect(desligada.outboundDispatch).toBe(false);
  });

  it('o perfil gravado desligado abre o formulário desligado', () => {
    expect(formFromProfile({ ...profile, outboundDispatch: false }).outboundDispatch).toBe(false);
  });

  it('uma resposta sem o campo abre o formulário ligada, que é o comportamento de sempre', () => {
    const { outboundDispatch: _omitido, ...semCampo } = profile;

    expect(formFromProfile(semCampo as ConnectorProfile).outboundDispatch).toBe(true);
  });

  it('virar a chave conta como edição pendente, e o botão de testar credencial pede para salvar antes', () => {
    const form = { ...formFromProfile(profile), outboundDispatch: false };

    expect(hasPendingEdit(form, {}, profile)).toBe(true);
  });
});

describe('os módulos e as integrações do conector (6.13)', () => {
  it('o payload não tem a lista de módulos do tenant, e os do conector vão nele', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {});

    expect(payload).not.toHaveProperty('modules');   // o servidor a recusaria (D7): os módulos são do conector
    expect(connector(payload).modules).toEqual(['Fiscal', 'Contabil']);
    expect(connector(payload).integrations).toEqual([
      { key: 'notas-fiscais' },                                   // o caminho à espera do host não vai: o servidor o mantém
      { key: 'plano-de-contas', endpoint: '', batchSize: 100 },   // o vazio vai: é a integração inativa escolhida na tela
    ]);
  });

  it('o caminho editado vai, e apagado vai vazio, e nunca nulo', () => {
    const edited = editConnector(formFromProfile(profile), (c) => setIntegration(c, 'notas-fiscais', { endpoint: 'taxcompliance/v2/fiscal/dfe' }));
    const blanked = editConnector(formFromProfile(profile), (c) => setIntegration(c, 'notas-fiscais', { endpoint: '' }));

    expect(connector(buildConnectorPayload(edited, {})).integrations?.[0]).toEqual({ key: 'notas-fiscais', endpoint: 'taxcompliance/v2/fiscal/dfe' });
    expect(connector(buildConnectorPayload(blanked, {})).integrations?.[0]).toEqual({ key: 'notas-fiscais', endpoint: '' });
  });

  it('desligar um módulo tira as integrações dele', () => {
    const form = editConnector(formFromProfile(profile), (c) => toggleModule(c, 'Contabil', false));

    const payload = buildConnectorPayload(form, {});

    expect(connector(payload).modules).toEqual(['Fiscal']);
    expect(connector(payload).integrations?.map((i) => i.key)).toEqual(['notas-fiscais']);
  });

  it('ligar uma integração a liga sem caminho, e só com o módulo dela ligado', () => {
    const on = editConnector(formFromProfile(profile), (c) => toggleIntegration(c, 'lancamentos', true));
    const withoutModule = editConnector(formFromProfile(profile), (c) => toggleIntegration(c, 'k200', true));

    expect(connector(buildConnectorPayload(on, {})).integrations).toContainEqual({ key: 'lancamentos', endpoint: '' });
    expect(connector(buildConnectorPayload(withoutModule, {})).integrations?.map((i) => i.key)).not.toContain('k200');
  });

  it('o lote só vai onde a integração aceita, como número', () => {
    let form = editConnector(formFromProfile(profile), (c) => setIntegration(c, 'plano-de-contas', { batchSize: '50' }));
    form = editConnector(form, (c) => setIntegration(c, 'notas-fiscais', { batchSize: '10' }));

    const integrations = connector(buildConnectorPayload(form, {})).integrations ?? [];

    expect(integrations.find((i) => i.key === 'plano-de-contas')?.batchSize).toBe(50);
    expect(integrations.find((i) => i.key === 'notas-fiscais')).not.toHaveProperty('batchSize');
  });

  it('as integrações ligadas sem caminho são as pendências da tela', () => {
    const shown = shownConnector(shownAccount(formFromProfile(profile)));

    expect(pendingIntegrations(shown)).toEqual(['notas-fiscais', 'plano-de-contas']);
    expect(pendingIntegrations(setIntegration(shown, 'notas-fiscais', { endpoint: 'dfe' }))).toEqual(['plano-de-contas']);
  });
});

describe('as pendências da conta', () => {
  const field = (key: string) => ACCOUNT_FIELDS.find((f) => f.key === key)!;

  it('o endpoint de token vazio é pendência, e diz o efeito', () => {
    // A M2 deixa o tokenUrl nulo em todo tenant (D8): sem marca, quem migra cai na tela com o conector parado e sem saber por quê.
    expect(fieldPending(field('tokenUrl'), {})).toBe('Sem o endpoint de token, o conector não despacha');
    expect(fieldPending(field('tokenUrl'), { tokenUrl: '   ' })).toBe('Sem o endpoint de token, o conector não despacha');
    expect(fieldPending(field('tokenUrl'), { tokenUrl: 'https://login/oauth/token' })).toBeNull();
  });

  it('o Client ID vazio também, e o segredo fica com a marca dele, de configurado ou não', () => {
    expect(fieldPending(field('clientId'), {})).toBe('Sem o Client ID, o conector não despacha');
    expect(fieldPending(field('clientSecret'), {})).toBeNull();
  });

  it('o teste da conta diz o que falta na credencial gravada, em vez de chamar o servidor', () => {
    const missingToken = { ...profile, accounts: [{ ...profile.accounts[0], tokenUrl: null }] };
    const missingSecret = { ...profile, secrets: { ...profile.secrets, 'platform.Avalara.clientSecret': { configured: false, updatedOn: null } } };
    const missingBoth = { ...missingSecret, accounts: missingToken.accounts };

    expect(platformTestBlocker(missingToken, 'Avalara')).toBe('Falta o endpoint de token. Grave-o para testar.');
    expect(platformTestBlocker(missingSecret, 'Avalara')).toBe('Falta o Client Secret. Grave-o para testar.');
    expect(platformTestBlocker(missingBoth, 'Avalara')).toBe('Faltam o Client Secret e o endpoint de token. Grave-os para testar.');
    expect(platformTestBlocker({ ...profile, accounts: [] }, 'Avalara')).toBe('Falta a conta de plataforma. Grave-a para testar.');
    expect(platformTestBlocker(profile, 'Avalara')).toBeNull();
  });
});

describe('a edição pendente', () => {
  it('não há logo depois de carregar', () => {
    expect(hasPendingEdit(formFromProfile(profile), {}, profile)).toBe(false);
  });

  it('há depois de mudar um campo', () => {
    const form = formFromProfile(profile);
    form.inboundValues = setPath(form.inboundValues, 'auth.clientId', 'outro-app');

    expect(hasPendingEdit(form, {}, profile)).toBe(true);
  });

  it('há depois de mudar o endpoint de token da conta', () => {
    const form = formFromProfile(profile);

    const edited = withAccount(form, { ...shownAccount(form), values: setPath(shownAccount(form).values, 'tokenUrl', 'https://outro/token') });

    expect(hasPendingEdit(edited, {}, profile)).toBe(true);
  });

  it('há depois de digitar um segredo, e não com o campo apagado', () => {
    expect(hasPendingEdit(formFromProfile(profile), { 'platform.Avalara.clientSecret': 'n' }, profile)).toBe(true);
    expect(hasPendingEdit(formFromProfile(profile), { 'platform.Avalara.clientSecret': '' }, profile)).toBe(false);
  });

  it('há depois de mudar os módulos do conector', () => {
    const form = editConnector(formFromProfile(profile), (c) => toggleModule(c, 'Inventario', true));

    expect(hasPendingEdit(form, {}, profile)).toBe(true);
  });

  it('some depois de salvar: o perfil relido monta o mesmo payload', () => {
    let form = formFromProfile(profile);
    form.inboundValues = setPath(form.inboundValues, 'auth.clientId', 'outro-app');
    form = withAccount(form, { ...shownAccount(form), values: setPath(shownAccount(form).values, 'clientId', 'xyz') });
    const payload = buildConnectorPayload(form, {});
    const saved: ConnectorProfile = {
      ...profile,
      inboundSettings: payload.inboundSettings ?? '{}',
      accounts: (payload.accounts ?? []).map((a) => ({
        platform: a.platform,
        clientId: a.clientId,
        tokenUrl: a.tokenUrl,
        // Os módulos e as integrações não mudaram: o servidor devolve o que tinha.
        connectors: a.connectors.map((c) => ({ ...profile.accounts[0].connectors[0], adapter: c.adapter, settings: c.settings })),
      })),
    };

    expect(hasPendingEdit(formFromProfile(saved), {}, saved)).toBe(false);
  });
});
