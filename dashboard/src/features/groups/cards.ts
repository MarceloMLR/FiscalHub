import type { ModelTotals } from '../../types';

// Os cards pelo modelo (change erp-company-directory-and-card-filters, D9): o servidor conta, por modelo, todas as notas da
// janela; a tela soma os modelos em "todos" (model = null), ou mostra o escolhido. A ignorada já vem no total e fora do erro.

export interface CardSums {
  total: number;
  finalizadas: number;
  emProcessamento: number;
  comErro: number;
}

export function cardSums(totals: readonly ModelTotals[], model: string | null): CardSums {
  const rows = model === null ? totals : totals.filter((t) => t.model === model);
  return rows.reduce<CardSums>(
    (acc, t) => ({
      total: acc.total + t.total,
      finalizadas: acc.finalizadas + t.finalizadas,
      emProcessamento: acc.emProcessamento + t.emProcessamento,
      comErro: acc.comErro + t.comErro,
    }),
    { total: 0, finalizadas: 0, emProcessamento: 0, comErro: 0 },
  );
}

// As opções do filtro: os modelos que a janela tem, em ordem. O escolhido continua na lista quando a janela nova não o tem
// (os cards mostram 0), para a troca de período não trocar o filtro em silêncio.
export function modelOptions(totals: readonly ModelTotals[], selected: string | null): string[] {
  const models = new Set(totals.map((t) => t.model).filter((m): m is string => m !== null));
  if (selected !== null) {
    models.add(selected);
  }
  return [...models].sort();
}
