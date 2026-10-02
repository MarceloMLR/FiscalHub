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

// Os modelos que o hub conhece: os do mapa padrão do ERP (D365InboundSettings.DefaultModelTypes), o mesmo vocabulário que a
// descoberta grava no registro. Eles aparecem sempre no filtro, mesmo sem nenhuma nota: a primeira versão tirava as opções
// só das notas da janela, e num banco vazio, ou num dia sem nota, o dropdown vinha vazio (conferência na tela, 2026-10-02).
export const KNOWN_MODELS: readonly { code: string; name: string }[] = [
  { code: '55', name: 'NF-e' },
  { code: '57', name: 'CT-e' },
  { code: 'SE', name: 'NFS-e' },
];

// As opções do filtro: os modelos conhecidos, na ordem acima, e depois os que a janela traz e o hub não conhece (um
// modelTypes próprio do tenant), em ordem. O escolhido continua na lista quando a janela nova não o tem (os cards mostram
// 0), para a troca de período não trocar o filtro em silêncio.
export function modelOptions(totals: readonly ModelTotals[], selected: string | null): string[] {
  const known = KNOWN_MODELS.map((m) => m.code);
  const others = new Set(totals.map((t) => t.model).filter((m): m is string => m !== null && !known.includes(m)));
  if (selected !== null && !known.includes(selected)) {
    others.add(selected);
  }
  return [...known, ...[...others].sort()];
}

// O rótulo da opção: o código e o documento do modelo conhecido ("55 · NF-e"); o outro fica só com o código.
export function modelLabel(code: string): string {
  const known = KNOWN_MODELS.find((m) => m.code === code);
  return known ? `${code} · ${known.name}` : code;
}
