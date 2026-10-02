import { describe, expect, it } from 'vitest';
import { compareShownDays, formatDay, formatPeriod, periodParam, rowId } from './groupRow';
import type { DocumentGroup } from '../../types';

// A linha da tabela pela execução (change erp-company-directory-and-card-filters, D15).
const row = (over: Partial<DocumentGroup> = {}): DocumentGroup => ({
  companyCode: '44278225000260',
  branchCode: 'SP-01',
  executedOn: '2026-10-02',
  periodStart: null,
  periodEnd: null,
  type: 'GoodsInvoice55',
  model: '55',
  trigger: 'Automatic',
  total: 1,
  finalizadas: 1,
  emProcessamento: 0,
  comErro: 0,
  ...over,
});

const imediata = row({ trigger: 'Manual', periodStart: '2016-09-01', periodEnd: '2016-09-30' });

describe('a data e o período da linha', () => {
  it('o dia da execução aparece em dd/mm/aaaa, sem passar pelo fuso do navegador', () => {
    expect(formatDay('2026-10-02')).toBe('02/10/2026');
    expect(formatDay('2027-01-01')).toBe('01/01/2027');
  });

  it('a automática não tem período', () => {
    expect(formatPeriod(row())).toBe('—');
  });

  it('a integração mostra o período que integrou, e o de um dia só uma vez', () => {
    expect(formatPeriod(imediata)).toBe('01/09/2016 a 30/09/2016');
    expect(formatPeriod(row({ periodStart: '2026-10-01', periodEnd: '2026-10-01' }))).toBe('01/10/2026');
  });
});

describe('a linha no modal e na grade', () => {
  it('o modal recebe o período da linha, ou none na automática', () => {
    expect(periodParam(imediata)).toBe('2016-09-01_2016-09-30');
    expect(periodParam(row())).toBe('none');
  });

  it('duas execuções no mesmo dia, com períodos diferentes, são duas linhas', () => {
    const marco = row({ trigger: 'Manual', periodStart: '2016-03-01', periodEnd: '2016-03-31' });
    expect(new Set([rowId(imediata), rowId(marco), rowId(row())]).size).toBe(3);
  });

  it('as datas mostradas ordenam pela data, e não pelo texto', () => {
    const shown = ['02/10/2026', '30/09/2026', '01/01/2027'];
    expect([...shown].sort(compareShownDays)).toEqual(['30/09/2026', '02/10/2026', '01/01/2027']);
  });

  it('o período ordena pelo primeiro dia, com a automática antes', () => {
    const shown = ['01/09/2016 a 30/09/2016', '—', '01/03/2016 a 31/03/2016'];
    expect([...shown].sort(compareShownDays)).toEqual(['—', '01/03/2016 a 31/03/2016', '01/09/2016 a 30/09/2016']);
  });
});
