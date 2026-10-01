import { describe, expect, it } from 'vitest';
import type { ConnectorProfile } from '../../types';
import { SECRET_MASK, buildConnectorPayload, formFromProfile, hasPendingEdit, setPath } from './connectorPayload';

// O que a tela de Configurações envia (design D3): o segredo é campo de escrita pura, e o salvar sem digitar nada não
// leva o campo — nem vazio, nem com a máscara. É o lado da tela da prova; o do servidor está no
// ConnectorProfileServiceTests (o campo ausente preserva o segredo, e a máscara como valor é recusada).

// Um perfil como o GET /connector devolve: sem segredo e sem referência nas settings, e os três segredos configurados.
const profile: ConnectorProfile = {
  tenantId: 'tenant-a',
  environment: 'Sandbox',
  inboundAdapter: 'Dynamics365',
  inboundSettings: JSON.stringify({
    url: 'https://fiscosysdev.operations.dynamics.com',
    auth: { tenantId: 'entra', clientId: 'app' },
    poll: { enabled: true, startFrom: '2015-01-01T00:00:00Z' },
  }),
  outboundAdapter: 'Avalara',
  outboundSettings: JSON.stringify({
    sandbox: { baseUrl: 'https://sandbox.example/', clientId: 'abc' },
    production: { baseUrl: 'https://prod.example/', clientId: 'xyz' },
  }),
  supportAdapter: null,
  supportSettings: '{}',
  secrets: {
    'inbound.auth.clientSecret': { configured: true, updatedOn: '2026-09-28T17:05:00Z' },
    'outbound.sandbox.clientSecret': { configured: true, updatedOn: '2026-09-28T17:05:00Z' },
    'outbound.production.clientSecret': { configured: true, updatedOn: '2026-09-28T17:05:00Z' },
  },
  modules: ['Fiscal'],
};

const MASK_CHARACTERS = ['*', '•', '●', '∗'];

function sent(payload: ReturnType<typeof buildConnectorPayload>) {
  return JSON.stringify(payload);
}

describe('o payload do perfil', () => {
  it('salvar sem digitar não leva nenhum segredo nem a máscara, na entrada e nas duas seções da saída', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {});

    expect(payload.inboundSettings).not.toContain('clientSecret');
    expect(payload.outboundSettings).not.toContain('clientSecret');
    expect(sent(payload)).not.toContain(SECRET_MASK);
    for (const c of MASK_CHARACTERS) {
      expect(sent(payload)).not.toContain(c);
    }
  });

  it('mudar só a URL base também não leva o segredo', () => {
    const form = formFromProfile(profile);
    form.sandboxValues = setPath(form.sandboxValues, 'baseUrl', 'https://outra.example/');

    const payload = buildConnectorPayload(form, {});

    expect(payload.outboundSettings).toContain('https://outra.example/');
    expect(payload.outboundSettings).not.toContain('clientSecret');
    expect(payload.inboundSettings).not.toContain('clientSecret');
  });

  it('o segredo digitado vai, e só ele', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), { 'outbound.sandbox.clientSecret': 'n0v0' });

    const outbound = JSON.parse(payload.outboundSettings ?? '{}');
    expect(outbound.sandbox.clientSecret).toBe('n0v0');
    expect(outbound.production).not.toHaveProperty('clientSecret');
    expect(payload.inboundSettings).not.toContain('clientSecret');
  });

  it('o campo digitado e apagado conta como não digitado', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), { 'inbound.auth.clientSecret': '' });

    expect(payload.inboundSettings).not.toContain('clientSecret');
  });

  it('as settings que a tela não mostra voltam intactas, e os módulos vão junto', () => {
    const payload = buildConnectorPayload(formFromProfile(profile), {});

    expect(JSON.parse(payload.inboundSettings ?? '{}').poll).toEqual({ enabled: true, startFrom: '2015-01-01T00:00:00Z' });
    expect(payload.modules).toEqual(['Fiscal']);
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

  it('há depois de digitar um segredo, e não com o campo apagado', () => {
    expect(hasPendingEdit(formFromProfile(profile), { 'outbound.sandbox.clientSecret': 'n' }, profile)).toBe(true);
    expect(hasPendingEdit(formFromProfile(profile), { 'outbound.sandbox.clientSecret': '' }, profile)).toBe(false);
  });

  it('há depois de mudar os módulos', () => {
    const form = { ...formFromProfile(profile), modules: ['Fiscal', 'Inventario'] as ConnectorProfile['modules'] };

    expect(hasPendingEdit(form, {}, profile)).toBe(true);
  });

  it('some depois de salvar: o perfil relido monta o mesmo payload', () => {
    const form = formFromProfile(profile);
    form.inboundValues = setPath(form.inboundValues, 'auth.clientId', 'outro-app');
    const payload = buildConnectorPayload(form, {});
    const saved: ConnectorProfile = {
      ...profile,
      inboundSettings: payload.inboundSettings ?? '{}',
      outboundSettings: payload.outboundSettings ?? '{}',
    };

    expect(hasPendingEdit(formFromProfile(saved), {}, saved)).toBe(false);
  });
});
