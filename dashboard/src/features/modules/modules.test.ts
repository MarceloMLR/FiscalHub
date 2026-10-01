import { describe, expect, it } from 'vitest';
import { entryView, visibleModuleViews } from './modules';

// A barra lateral pelos módulos do tenant (spec module-navigation): o Agendamento sempre, e a tela de entrada é o Fiscal,
// ou o primeiro módulo que o tenant tem.
describe('o bloco Integrações', () => {
  it('um tenant só com o Fiscal vê Fiscal e Agendamento, e entra no Fiscal', () => {
    expect(visibleModuleViews(['Fiscal']).map((m) => m.label)).toEqual(['Fiscal', 'Agendamento']);
    expect(entryView(['Fiscal'])).toBe('documents');
  });

  it('com os três módulos, os quatro sub-blocos na ordem', () => {
    expect(visibleModuleViews(['Fiscal', 'Contabil', 'Inventario']).map((m) => m.label)).toEqual([
      'Fiscal',
      'Contábil',
      'Inventário',
      'Agendamento',
    ]);
  });

  it('sem o Fiscal, entra no primeiro módulo que o tenant tem', () => {
    expect(visibleModuleViews(['Inventario']).map((m) => m.label)).toEqual(['Inventário', 'Agendamento']);
    expect(entryView(['Inventario'])).toBe('inventory');
  });

  it('sem módulo nenhum, entra no Agendamento', () => {
    expect(entryView([])).toBe('integrations');
  });
});
