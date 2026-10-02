import type { DocumentGroup } from '../../types';

// A linha da tabela (change erp-company-directory-and-card-filters, D15): o dia da execução que trouxe as notas, e o período
// que ela integrou. A automática não tem período. Os dias chegam como aaaa-mm-dd, já no dia de Brasília, e a tela os mostra
// assim, sem passar pelo Date: o fuso do navegador não troca o dia, e o texto ordena como a data (conferência na tela,
// 2026-10-02: só as datas, aaaa-mm-dd, separadas por um traço).

type Period = Pick<DocumentGroup, 'periodStart' | 'periodEnd'>;

/** O período integrado, "aaaa-mm-dd – aaaa-mm-dd"; um dia só uma vez; "—" na automática. */
export function formatPeriod(g: Period): string {
  if (!g.periodStart || !g.periodEnd) {
    return '—';
  }
  return g.periodStart === g.periodEnd ? g.periodStart : `${g.periodStart} – ${g.periodEnd}`;
}

/** O período no parâmetro do modal: none na automática, e aaaa-mm-dd_aaaa-mm-dd na integração. */
export function periodParam(g: Period): string {
  return g.periodStart && g.periodEnd ? `${g.periodStart}_${g.periodEnd}` : 'none';
}

/** A chave da linha: empresa, filial, dia da execução, período, tipo, modelo e modo. */
export function rowId(g: DocumentGroup): string {
  return `${g.companyCode}:${g.branchCode}:${g.executedOn}:${periodParam(g)}:${g.type}:${g.model ?? ''}:${g.trigger}`;
}
