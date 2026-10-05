import { describe, expect, it } from 'vitest';
import { formatBranch, formatCompany } from './companyCode';

// A máscara pelo tamanho, e não pelo tipo do caractere (spec document-grouping, "CNPJ formatado na tela"): o CNPJ completo
// (14) e a raiz (8), numéricos ou alfanuméricos. Um formatador só, para o dropdown, as tabelas, os cards e o modal (change
// company-root-in-directory, D6).
describe('a empresa na tela', () => {
  it('o CNPJ numérico de 14 dígitos ganha a máscara', () => {
    expect(formatCompany('44278225000180')).toBe('44.278.225/0001-80');
  });

  it('o CNPJ alfanumérico de 14 caracteres ganha a mesma máscara', () => {
    expect(formatCompany('12ABC34501DE35')).toBe('12.ABC.345/01DE-35');
  });

  it('a raiz, de 8 caracteres, ganha a máscara da raiz', () => {
    expect(formatCompany('44278225')).toBe('44.278.225');
  });

  it('a raiz alfanumérica ganha a mesma máscara', () => {
    expect(formatCompany('12ABC345')).toBe('12.ABC.345');
  });

  it('a empresa do caminho de XML, que já é a raiz, deixa de aparecer como está', () => {
    expect(formatCompany('12345678')).toBe('12.345.678');
  });

  it('a empresa de outro tamanho fica como está', () => {
    expect(formatCompany('B01')).toBe('B01');
  });
});

// A filial no dropdown (change company-root-in-directory, revista em 2026-10-05): o CNPJ completo dela, com o código ao lado.
describe('a filial no dropdown', () => {
  it('com o CNPJ do D365, mostra o CNPJ mascarado e o código', () => {
    expect(formatBranch({ code: 'SP-01', name: 'Filial de serviços', taxId: '44278225000260' })).toBe('44.278.225/0002-60 — SP-01');
  });

  it('com o CNPJ alfanumérico, a mesma máscara', () => {
    expect(formatBranch({ code: 'ALFA-01', name: 'Filial nova', taxId: '12ABC34501DE35' })).toBe('12.ABC.345/01DE-35 — ALFA-01');
  });

  it('sem o CNPJ, como no diretório de exemplo, mostra o código e o nome', () => {
    expect(formatBranch({ code: '0001', name: 'Matriz', taxId: null })).toBe('0001 — Matriz');
    expect(formatBranch({ code: '0001', name: 'Matriz' })).toBe('0001 — Matriz');
  });
});
