import { describe, expect, it } from 'vitest';
import { formatCompany } from './companyCode';

// A máscara de CNPJ pelo tamanho, e não pelo tipo do caractere (spec document-grouping, "CNPJ formatado na tela"): o CNPJ
// alfanumérico tem 14 caracteres e ganha a mesma máscara.
describe('a empresa na tela', () => {
  it('o CNPJ numérico de 14 dígitos ganha a máscara', () => {
    expect(formatCompany('44278225000180')).toBe('44.278.225/0001-80');
  });

  it('o CNPJ alfanumérico de 14 caracteres ganha a mesma máscara', () => {
    expect(formatCompany('12ABC34501DE35')).toBe('12.ABC.345/01DE-35');
  });

  it('a empresa de outro tamanho, como a de 8 dígitos do XML, fica como está', () => {
    expect(formatCompany('12345678')).toBe('12345678');
  });
});
