// A janela dos cards (change erp-company-directory-and-card-filters, D11): os últimos N dias são hoje e os N−1 anteriores,
// inclusive, pelo dia do relógio de quem vê o dashboard. O "Dia" é N = 1, e é o padrão. A data comparada é a data fiscal
// da nota, no fuso de quem emitiu, sem conversão (spec document-grouping).

export interface Period {
  days: number;
  label: string;
  // O que a nota de cada card diz sobre a janela.
  note: string;
}

export const PERIODS: readonly Period[] = [
  { days: 1, label: 'Dia', note: 'no dia de hoje' },
  { days: 7, label: '7 dias', note: 'nos últimos 7 dias' },
  { days: 15, label: '15 dias', note: 'nos últimos 15 dias' },
  { days: 30, label: '30 dias', note: 'nos últimos 30 dias' },
];

function iso(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

// A janela em dias do calendário local: o construtor de Date resolve a virada de mês e de ano.
export function periodWindow(days: number, today: Date): { from: string; to: string } {
  const first = new Date(today.getFullYear(), today.getMonth(), today.getDate() - (days - 1));
  return { from: iso(first), to: iso(today) };
}

// O período personalizado (pedido da conferência na tela, 2026-10-02): de uma data a outra, inclusive, no formato do
// <input type="date">. O problema é dito na tela, e a contagem não é pedida. As datas aaaa-mm-dd se comparam como texto.
export function customRangeProblem(from: string, to: string): string | null {
  if (from === '' || to === '') {
    return 'Informe as duas datas do período.';
  }
  return from > to ? 'A data inicial é depois da final.' : null;
}
