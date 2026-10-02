import { describe, expect, it } from 'vitest';
import { formatPeriod, periodParam, rowId } from './groupRow';
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

const imediata = row({ trigger: 'Manual', periodStart: '2016-09-01', periodEnd: '2016-10-02' });

describe('o período da linha', () => {
  it('a automática não tem período', () => {
    expect(formatPeriod(row())).toBe('—');
  });

  it('a integração mostra só as datas, aaaa-mm-dd, separadas por um traço', () => {
    expect(formatPeriod(imediata)).toBe('2016-09-01 – 2016-10-02');
  });

  it('o período de um dia só mostra o dia uma vez', () => {
    expect(formatPeriod(row({ periodStart: '2026-10-01', periodEnd: '2026-10-01' }))).toBe('2026-10-01');
  });

  it('o texto mostrado ordena como a data', () => {
    const shown = ['2016-09-01 – 2016-10-02', '2016-03-01 – 2016-03-31', '2026-10-01'];
    expect([...shown].sort()).toEqual(['2016-03-01 – 2016-03-31', '2016-09-01 – 2016-10-02', '2026-10-01']);
  });
});

describe('a linha no modal e na grade', () => {
  it('o modal recebe o período da linha, ou none na automática', () => {
    expect(periodParam(imediata)).toBe('2016-09-01_2016-10-02');
    expect(periodParam(row())).toBe('none');
  });

  it('duas execuções no mesmo dia, com períodos diferentes, são duas linhas', () => {
    const marco = row({ trigger: 'Manual', periodStart: '2016-03-01', periodEnd: '2016-03-31' });
    expect(new Set([rowId(imediata), rowId(marco), rowId(row())]).size).toBe(3);
  });
});
