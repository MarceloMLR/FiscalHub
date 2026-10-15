import { describe, expect, it } from 'vitest';
import type { ConnectorInfo, ModuleName } from '../../types';
import {
  NO_CONNECTOR_LABEL,
  entryItem,
  navigationGroups,
  navigationItems,
  shownItem,
  type ModuleView,
} from './modules';

// O modelo de navegação da barra lateral (spec module-navigation; design D10 e D11): a árvore de grupos e itens montada
// a partir da árvore de conectores do /info. Cada item carrega a identidade (conector, módulo), o rótulo, o título, o
// subtítulo e a tela que abre — e é por isso que o item e a tela não podem se desalinhar.

const connector = (adapter: string, label: string, modules: ModuleName[], scheduling = true): ConnectorInfo =>
  ({ adapter, label, scheduling, modules });

const taxCompliance = connector('Avalara', 'Tax Compliance', ['Fiscal']);

const labels = (connectors: ConnectorInfo[] | undefined) =>
  navigationGroups(connectors).map((g) => [g.label, ...g.items.map((i) => i.label)]);

describe('4.1 a árvore de grupos e itens', () => {
  it('um conector e um módulo: o grupo aparece mesmo assim', () => {
    expect(labels([taxCompliance])).toEqual([['Tax Compliance', 'Fiscal', 'Agendamento']]);
  });

  it('os módulos na ordem Fiscal, Contábil, Inventário, qualquer que seja a do perfil', () => {
    expect(labels([connector('Avalara', 'Tax Compliance', ['Inventario', 'Fiscal'])])).toEqual([
      ['Tax Compliance', 'Fiscal', 'Inventário', 'Agendamento'],
    ]);
  });

  it('dois conectores, dois grupos, com o Fiscal em cada um', () => {
    const groups = navigationGroups([taxCompliance, connector('AvalaraAvaTax', 'AvaTax', ['Fiscal', 'Contabil'], false)]);

    expect(groups.map((g) => [g.label, ...g.items.map((i) => i.label)])).toEqual([
      ['Tax Compliance', 'Fiscal', 'Agendamento'],
      ['AvaTax', 'Fiscal', 'Contábil'],
    ]);

    // O Fiscal aparece uma vez em cada grupo, e os dois são itens diferentes: a identidade é o par (conector, módulo).
    const fiscais = navigationItems(groups).filter((i) => i.module === 'Fiscal');
    expect(fiscais.map((i) => i.connector)).toEqual(['Avalara', 'AvalaraAvaTax']);
    expect(new Set(fiscais.map((i) => i.key)).size).toBe(2);
  });

  it('o conector que não agenda não tem o item de Agendamento, e o que agenda continua tendo', () => {
    expect(labels([taxCompliance, connector('AvalaraAvaTax', 'AvaTax', ['Fiscal'], false)])).toEqual([
      ['Tax Compliance', 'Fiscal', 'Agendamento'],
      ['AvaTax', 'Fiscal'],
    ]);
  });

  it('o conector sem módulo e sem agendamento não vira grupo', () => {
    expect(labels([taxCompliance, connector('AvalaraAvaTax', 'AvaTax', [], false)])).toEqual([
      ['Tax Compliance', 'Fiscal', 'Agendamento'],
    ]);
  });

  // A varredura que trava o descuido do ADR-0039: um item apontando para a tela que não é a dele. A tabela abaixo é
  // escrita à mão, de propósito — comparar o modelo com ele mesmo não provaria nada.
  it('a árvore inteira, item a item: cada um abre a tela que declara', () => {
    const esperado: Record<string, { view: ModuleView; title: string; subtitle: string }> = {
      Fiscal: { view: 'documents', title: 'Fiscal', subtitle: 'Notas fiscais integradas e seus status' },
      Contábil: { view: 'accounting', title: 'Contábil', subtitle: 'Módulo reservado' },
      Inventário: { view: 'inventory', title: 'Inventário', subtitle: 'Módulo reservado' },
      Agendamento: {
        view: 'integrations',
        title: 'Agendamento',
        subtitle: 'Dispare agora ou agende, e acompanhe as execuções',
      },
    };

    const groups = navigationGroups([
      connector('Avalara', 'Tax Compliance', ['Fiscal', 'Contabil', 'Inventario']),
      connector('AvalaraAvaTax', 'AvaTax', ['Fiscal', 'Contabil'], false),
    ]);
    const items = navigationItems(groups);

    for (const item of items) {
      expect({ view: item.view, title: item.title, subtitle: item.subtitle }).toEqual(esperado[item.label]);
    }

    // E a varredura cobriu os quatro rótulos: uma tabela que ninguém percorre não trava nada.
    expect(new Set(items.map((i) => i.label))).toEqual(new Set(Object.keys(esperado)));
    expect(items).toHaveLength(6);
  });
});

describe('4.2 o grupo que nomeia a ausência', () => {
  it('tenant sem conector: um grupo que nomeia a ausência, com o Fiscal e o Agendamento', () => {
    expect(labels([])).toEqual([[NO_CONNECTOR_LABEL, 'Fiscal', 'Agendamento']]);
    expect(entryItem(navigationGroups([])).label).toBe('Fiscal');
  });

  it('o único conector não produz item: o mesmo grupo, e aquele conector não vira grupo', () => {
    expect(labels([connector('Avalara', 'Tax Compliance', [], false)])).toEqual([
      [NO_CONNECTOR_LABEL, 'Fiscal', 'Agendamento'],
    ]);
  });

  it('o grupo some quando um conector passa a produzir item', () => {
    expect(labels([taxCompliance]).map((g) => g[0])).toEqual(['Tax Compliance']);
  });

  it('o rótulo nomeia a ausência, e não inventa um conector', () => {
    expect(NO_CONNECTOR_LABEL.toLowerCase()).toContain('sem conector');
  });
});

describe('4.8 enquanto o /info não respondeu', () => {
  // A barra lateral não afirma nada sobre a configuração do cliente antes de saber — o mesmo cuidado que o D7 tem com
  // os botões do JSON e o selo de ambiente tem com o rodapé. `undefined` é "ainda não se sabe"; a árvore vazia é uma
  // resposta, e é ela que nomeia a ausência.
  it('não desenha grupo nenhum', () => {
    expect(navigationGroups(undefined)).toEqual([]);
  });

  it('não afirma ausência de conector: o grupo que a nomeia não está lá', () => {
    expect(navigationGroups(undefined).map((g) => g.label)).not.toContain(NO_CONNECTOR_LABEL);
    expect(navigationItems(navigationGroups(undefined))).toEqual([]);
  });

  it('"ainda não respondeu" e "respondeu a árvore vazia" são coisas diferentes', () => {
    expect(labels(undefined)).toEqual([]);
    expect(labels([])).toEqual([[NO_CONNECTOR_LABEL, 'Fiscal', 'Agendamento']]);
  });

  it('depois da resposta, o comportamento já especificado vale sem mudança', () => {
    expect(labels([taxCompliance])).toEqual([['Tax Compliance', 'Fiscal', 'Agendamento']]);
    // Inclusive a árvore vazia, que continua mostrando o grupo da ausência, com o Fiscal como tela de entrada.
    expect(entryItem(navigationGroups([])).label).toBe('Fiscal');
  });
});

describe('4.3 a tela de entrada e o item que some', () => {
  it('a tela aberta é o primeiro item do primeiro grupo', () => {
    const groups = navigationGroups([connector('Avalara', 'Tax Compliance', ['Contabil', 'Fiscal'])]);

    expect(entryItem(groups).label).toBe('Fiscal');
    expect(entryItem(groups).connector).toBe('Avalara');
    expect(shownItem(groups, null)).toEqual(entryItem(groups));
  });

  it('sem o Fiscal, a entrada é o primeiro item que o conector tem', () => {
    const groups = navigationGroups([connector('Avalara', 'Tax Compliance', ['Inventario'])]);

    expect(entryItem(groups).label).toBe('Inventário');
  });

  it('o item selecionado que deixa de existir cai na tela de entrada', () => {
    const antes = navigationGroups([connector('Avalara', 'Tax Compliance', ['Fiscal', 'Inventario'])]);
    const inventario = navigationItems(antes).find((i) => i.module === 'Inventario')!;

    // O Admin desliga o Inventário no conector enquanto o usuário o está vendo.
    const depois = navigationGroups([taxCompliance]);

    expect(shownItem(antes, inventario.key)).toEqual(inventario);
    expect(shownItem(depois, inventario.key)).toEqual(entryItem(depois));
  });

  it('o conector que saiu leva o item dele junto', () => {
    const antes = navigationGroups([taxCompliance, connector('AvalaraAvaTax', 'AvaTax', ['Fiscal'], false)]);
    const avaTax = navigationItems(antes).find((i) => i.connector === 'AvalaraAvaTax')!;

    expect(shownItem(navigationGroups([taxCompliance]), avaTax.key).connector).toBe('Avalara');
  });
});
