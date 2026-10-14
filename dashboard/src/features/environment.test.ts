import { describe, expect, it } from 'vitest';
import { environmentBadge } from './environment';

// O ambiente é da instância, e o selo só afirma o que o /info disse (change platform-account-and-connectors, D6).
describe('environmentBadge', () => {
  it('não afirma ambiente nenhum antes de o /info responder, ou se ele falhou', () => {
    expect(environmentBadge(undefined)).toBeNull();
  });

  it('mostra o Sandbox e a Produção como o /info os diz', () => {
    expect(environmentBadge('Sandbox')).toEqual({ isProd: false, label: 'Sandbox' });
    expect(environmentBadge('Production')).toEqual({ isProd: true, label: 'Produção' });
  });

  it('não completa um valor que não conhece', () => {
    expect(environmentBadge('')).toBeNull();
    expect(environmentBadge('Homologacao')).toBeNull();
  });
});
