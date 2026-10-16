import { describe, expect, it } from 'vitest';
import { canViewRawJson } from './rawJson';
import type { InfoResponse } from '../types';

// Quem vê as fotos cruas é decisão do servidor (ADR-0039 §5; change connector-navigation-and-admin-json, D7). A tela só
// obedece ao /info — e, como o selo de ambiente, não afirma nada antes de ele responder.
describe('canViewRawJson', () => {
  const info = (canViewRawJson: boolean): InfoResponse => ({
    environment: 'Sandbox',
    automaticIntegration: false,
    inboundScans: false,
    connectors: [{ adapter: 'Avalara', label: 'Tax Compliance', scheduling: true, modules: ['Fiscal'] }],
    canViewRawJson,
    outboundDispatch: true,
  });

  it('não mostra os botões antes de o /info responder, nem se ele falhou', () => {
    // undefined é o que o useInfo dá enquanto carrega e quando a consulta falha: nos dois casos, nada aparece — para
    // papel nenhum. Um botão que promete o que o servidor vai recusar com 403 é promessa quebrada.
    expect(canViewRawJson(undefined)).toBe(false);
  });

  it('mostra os botões quando o /info diz que o usuário logado pode ver as fotos cruas', () => {
    expect(canViewRawJson(info(true))).toBe(true);
  });

  it('não mostra os botões quando o /info diz que não', () => {
    expect(canViewRawJson(info(false))).toBe(false);
  });

  it('não decide por conta própria: a resposta não traz papel nenhum para a tela comparar', () => {
    // A tela não recebe a lista de papéis, e não há parâmetro de papel aqui: o servidor responde a pergunta, e não os
    // dados para a pergunta. Uma resposta antiga, sem o campo, vale como "não pode".
    const semOCampo = { environment: 'Sandbox', automaticIntegration: false, inboundScans: false, connectors: [] };
    expect(canViewRawJson(semOCampo as unknown as InfoResponse)).toBe(false);
  });
});
