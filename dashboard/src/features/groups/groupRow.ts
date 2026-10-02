import type { DocumentGroup } from '../../types';

// A linha da tabela (change erp-company-directory-and-card-filters, D15): o dia da execução que trouxe as notas, e o período
// que ela integrou. A automática não tem período. Os dias chegam como aaaa-mm-dd, já no dia de Brasília: a tela não os passa
// pelo Date, para o fuso do navegador não trocar o dia.

type Period = Pick<DocumentGroup, 'periodStart' | 'periodEnd'>;

/** aaaa-mm-dd → dd/mm/aaaa. */
export function formatDay(day: string): string {
  const [y, m, d] = day.split('-');
  return y && m && d ? `${d}/${m}/${y}` : day;
}

/** O período integrado, em dd/mm/aaaa; "—" na automática. */
export function formatPeriod(g: Period): string {
  if (!g.periodStart || !g.periodEnd) {
    return '—';
  }
  return g.periodStart === g.periodEnd
    ? formatDay(g.periodStart)
    : `${formatDay(g.periodStart)} a ${formatDay(g.periodEnd)}`;
}

/** O período no parâmetro do modal: none na automática, e aaaa-mm-dd_aaaa-mm-dd na integração. */
export function periodParam(g: Period): string {
  return g.periodStart && g.periodEnd ? `${g.periodStart}_${g.periodEnd}` : 'none';
}

/** A chave da linha: empresa, filial, dia da execução, período, tipo, modelo e modo. */
export function rowId(g: DocumentGroup): string {
  return `${g.companyCode}:${g.branchCode}:${g.executedOn}:${periodParam(g)}:${g.type}:${g.model ?? ''}:${g.trigger}`;
}

// O primeiro dd/mm/aaaa do texto mostrado, como aaaa-mm-dd; vazio no "—".
const sortKey = (shown: string) => {
  const m = /(\d{2})\/(\d{2})\/(\d{4})/.exec(shown);
  return m ? `${m[3]}-${m[2]}-${m[1]}` : '';
};

/**
 * A ordem das colunas de data pelo texto mostrado: a grade filtra pelo que a tela mostra (dd/mm/aaaa), e ordena pela
 * data. No período, ordena pelo primeiro dia, com a automática ("—") antes.
 */
export function compareShownDays(a: string, b: string): number {
  return sortKey(a).localeCompare(sortKey(b));
}
