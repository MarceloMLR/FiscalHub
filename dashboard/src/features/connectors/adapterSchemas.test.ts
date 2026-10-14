import { describe, expect, it } from 'vitest';
import { ACCOUNT_FIELDS, CONNECTOR_ADAPTERS, CREDENTIAL_TEST_ADAPTERS, INBOUND_ADAPTERS, fieldPlaceholder, type AdapterField } from './adapterSchemas';

// O seletor de plataforma oferece só o que o servidor aceita (change platform-account-and-connectors, D17): a gravação
// recusa a plataforma que nenhuma implementação de adapter declara, e uma opção que sempre falha não fica na tela.
describe('as plataformas da tela', () => {
  it('são só as declaradas no servidor, sem o Mock', () => {
    expect(Object.keys(CONNECTOR_ADAPTERS)).toEqual(['Avalara']);
  });

  it('a Avalara tem teste de credencial da conta', () => {
    expect(CREDENTIAL_TEST_ADAPTERS.platform.has('Avalara')).toBe(true);
  });
});

// O exemplo das URLs da Avalara segue o ambiente da instância, o do /info. Numa instância Sandbox, uma URL de produção
// como exemplo é a que um Admin apressado copia, e autentica contra produção numa instância que se anuncia Sandbox. Sem o
// /info, nenhum exemplo: a tela não presume ambiente (D6), como o selo.
describe('os exemplos das URLs da Avalara seguem o ambiente da instância', () => {
  const byKey = (fields: AdapterField[], key: string) => fields.find((f) => f.key === key)!;
  const tokenUrl = byKey(ACCOUNT_FIELDS, 'tokenUrl');
  const bases = CONNECTOR_ADAPTERS.Avalara;

  it('numa instância Sandbox, os do sandbox', () => {
    expect(fieldPlaceholder(tokenUrl, 'Sandbox')).toBe('https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token');
    expect(bases.map((f) => fieldPlaceholder(f, 'Sandbox'))).toEqual([
      'https://api-gateway.sandbox.avalarabrasil.com.br/',
      'https://api-gateway.sandbox.avalarabrasil.com.br/',
    ]);
  });

  it('numa instância Production, os de produção', () => {
    expect(fieldPlaceholder(tokenUrl, 'Production')).toBe('https://api-gateway.avalarabrasil.com.br/oauth/token');
    expect(bases.map((f) => fieldPlaceholder(f, 'Production'))).toEqual([
      'https://api-gateway.avalarabrasil.com.br/',
      'https://api-gateway.avalarabrasil.com.br/',
    ]);
  });

  it('antes de o /info responder, ou com um ambiente que ele não conhece, não há exemplo', () => {
    expect(fieldPlaceholder(tokenUrl, undefined)).toBeUndefined();
    expect(fieldPlaceholder(tokenUrl, 'Hml')).toBeUndefined();
    expect(bases.map((f) => fieldPlaceholder(f, undefined))).toEqual([undefined, undefined]);
  });

  it('nenhum campo da conta ou do conector traz uma URL da Avalara fixa, fora do ambiente', () => {
    const fields = [...ACCOUNT_FIELDS, ...Object.values(CONNECTOR_ADAPTERS).flat()];

    expect(fields.filter((f) => f.placeholder?.includes('avalarabrasil')).map((f) => f.key)).toEqual([]);
  });

  it('o campo sem exemplo por ambiente mantém o dele', () => {
    expect(fieldPlaceholder(byKey(INBOUND_ADAPTERS.Dynamics365, 'url'), 'Sandbox')).toBe('https://empresa.operations.dynamics.com');
  });
});
