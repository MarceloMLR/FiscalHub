import { describe, expect, it } from 'vitest';
import { cardSums, modelLabel, modelOptions } from './cards';
import type { ModelTotals } from '../../types';

const nfe: ModelTotals = { model: '55', total: 5, finalizadas: 0, emProcessamento: 0, comErro: 5 };
const nfse: ModelTotals = { model: 'SE', total: 9, finalizadas: 0, emProcessamento: 0, comErro: 0 };

// Os cards pelo modelo (spec document-grouping, design D9): as contagens vêm do servidor, por modelo; a tela soma os
// modelos em "todos", ou mostra o escolhido.
describe('os cards', () => {
  it('em todos os modelos, somam as linhas', () => {
    expect(cardSums([nfe, nfse], null)).toEqual({ total: 14, finalizadas: 0, emProcessamento: 0, comErro: 5 });
  });

  it('num modelo só, mostram a linha dele, com a ignorada no total e fora do erro', () => {
    expect(cardSums([nfe, nfse], 'SE')).toEqual({ total: 9, finalizadas: 0, emProcessamento: 0, comErro: 0 });
    expect(cardSums([nfe, nfse], '55')).toEqual({ total: 5, finalizadas: 0, emProcessamento: 0, comErro: 5 });
  });

  it('o modelo que o período não tem dá zero', () => {
    expect(cardSums([nfe], 'SE')).toEqual({ total: 0, finalizadas: 0, emProcessamento: 0, comErro: 0 });
  });

  it('o registro antigo, sem modelo, conta em todos', () => {
    expect(cardSums([nfe, { ...nfse, model: null }], null).total).toBe(14);
  });
});

describe('as opções de modelo', () => {
  it('trazem sempre os modelos que o hub conhece, mesmo sem nenhuma nota', () => {
    expect(modelOptions([], null)).toEqual(['55', '57', 'SE']);
  });

  it('acrescentam os modelos da janela que o hub não conhece, sem repetir os conhecidos', () => {
    expect(modelOptions([nfse, nfe, { ...nfe, model: '65' }, { ...nfe, model: null }], null)).toEqual(['55', '57', 'SE', '65']);
  });

  it('guardam o escolhido quando a janela nova não o tem', () => {
    expect(modelOptions([nfe], '65')).toEqual(['55', '57', 'SE', '65']);
    expect(modelOptions([], 'SE')).toEqual(['55', '57', 'SE']);
  });

  it('o rótulo diz o documento do modelo conhecido, e o outro fica só com o código', () => {
    expect(['55', '57', 'SE', '65'].map(modelLabel)).toEqual(['55 · NF-e', '57 · CT-e', 'SE · NFS-e', '65']);
  });
});
