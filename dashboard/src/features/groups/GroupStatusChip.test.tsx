import { describe, expect, it } from 'vitest';
import { groupStatus } from './GroupStatusChip';
import type { DocumentGroup } from '../../types';

// O selo da linha da tabela de grupos, derivado das contagens (spec document-grouping). O caso que esta change conserta:
// um grupo só de notas salvas dizia "Finalizado" com as três faixas zeradas — mentira de tela (ADR-0040).
//
// O que este arquivo NÃO conserta, e está nos Próximos passos: o mesmo defeito com "Ignorado". Um grupo de 10 NFS-e
// ignoradas continua dizendo "Finalizado" com tudo zerado, porque `Ignored` não tem contagem própria.

const GRUPO: DocumentGroup = {
  companyCode: '44278225',
  branchCode: '0001',
  executedOn: '2026-10-16',
  periodStart: null,
  periodEnd: null,
  type: 'GoodsInvoice55',
  model: '55',
  trigger: 'Automatic',
  total: 0,
  finalizadas: 0,
  emProcessamento: 0,
  comErro: 0,
  salvas: 0,
};

const grupo = (counts: Partial<DocumentGroup>): DocumentGroup => ({ ...GRUPO, ...counts });

describe('o selo do grupo', () => {
  it('só de salvas: diz "Salvas", e não "Finalizado"', () => {
    expect(groupStatus(grupo({ total: 10, salvas: 10 }))).toEqual({ label: 'Salvas', tone: 'pending' });
  });

  it('com salva e rejeitada: o erro manda, e diz "Com pendências"', () => {
    // É a rejeitada que precisa de alguém; a salva está parada de propósito.
    expect(groupStatus(grupo({ total: 8, salvas: 7, comErro: 1 })).label).toBe('Com pendências');
  });

  it('com salva e em processamento: o processamento vem antes', () => {
    expect(groupStatus(grupo({ total: 5, salvas: 3, emProcessamento: 2 })).label).toBe('Processando');
  });

  it('só de confirmadas: diz "Finalizado"', () => {
    expect(groupStatus(grupo({ total: 10, finalizadas: 10 }))).toEqual({ label: 'Finalizado', tone: 'ok' });
  });

  it('com salvas e finalizadas: diz "Salvas", porque ainda há nota que ninguém enviou', () => {
    expect(groupStatus(grupo({ total: 10, finalizadas: 7, salvas: 3 })).label).toBe('Salvas');
  });
});
