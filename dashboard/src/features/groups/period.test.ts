import { describe, expect, it } from 'vitest';
import { PERIODS, customRangeProblem, periodWindow } from './period';

// A janela dos cards (spec document-grouping, design D11): os últimos N dias são hoje e os N−1 anteriores, inclusive,
// pelo dia do navegador. O "Dia" é N = 1.
describe('a janela do período', () => {
  const today = new Date(2026, 8, 5, 15, 30); // 2026-09-05, à tarde, no relógio de quem vê

  it('o dia é só hoje', () => {
    expect(periodWindow(1, today)).toEqual({ from: '2026-09-05', to: '2026-09-05' });
  });

  it('7 e 15 dias contam hoje', () => {
    expect(periodWindow(7, today)).toEqual({ from: '2026-08-30', to: '2026-09-05' });
    expect(periodWindow(15, today)).toEqual({ from: '2026-08-22', to: '2026-09-05' });
  });

  it('30 dias a partir de 2026-09-05 começam em 2026-08-07', () => {
    expect(periodWindow(30, today)).toEqual({ from: '2026-08-07', to: '2026-09-05' });
  });

  it('atravessa a virada do ano', () => {
    expect(periodWindow(7, new Date(2027, 0, 3))).toEqual({ from: '2026-12-28', to: '2027-01-03' });
  });

  it('as opções são o dia, 7, 15 e 30 dias, com o dia primeiro', () => {
    expect(PERIODS.map((p) => p.days)).toEqual([1, 7, 15, 30]);
    expect(PERIODS[0].label).toBe('Dia');
  });
});

// O período personalizado (pedido da conferência na tela, 2026-10-02): de uma data a outra, inclusive. A tela diz o problema
// e não pede a contagem.
describe('o período personalizado', () => {
  it('pede as duas datas', () => {
    expect(customRangeProblem('', '2026-08-07')).toBe('Informe as duas datas do período.');
    expect(customRangeProblem('2015-01-01', '')).toBe('Informe as duas datas do período.');
  });

  it('recusa a data inicial depois da final', () => {
    expect(customRangeProblem('2026-08-08', '2026-08-07')).toBe('A data inicial é depois da final.');
  });

  it('aceita o intervalo válido, inclusive o de um dia só', () => {
    expect(customRangeProblem('2015-01-01', '2026-10-02')).toBeNull();
    expect(customRangeProblem('2026-08-07', '2026-08-07')).toBeNull();
  });
});
