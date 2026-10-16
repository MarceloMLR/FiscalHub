import { describe, expect, it } from 'vitest';
import { CARD_NOTES, cardSums, modelLabel, modelOptions } from './cards';
import type { ModelTotals } from '../../types';

const nfe: ModelTotals = { model: '55', total: 5, finalizadas: 0, emProcessamento: 0, comErro: 5, salvas: 0 };
const nfse: ModelTotals = { model: 'SE', total: 9, finalizadas: 0, emProcessamento: 0, comErro: 0, salvas: 0 };

// Os cards pelo modelo (spec document-grouping, design D9): as contagens vêm do servidor, por modelo; a tela soma os
// modelos em "todos", ou mostra o escolhido.
describe('os cards', () => {
  it('em todos os modelos, somam as linhas', () => {
    expect(cardSums([nfe, nfse], null)).toEqual({ total: 14, finalizadas: 0, emProcessamento: 0, comErro: 5, salvas: 0 });
  });

  it('num modelo só, mostram a linha dele, com a ignorada no total e fora do erro', () => {
    expect(cardSums([nfe, nfse], 'SE')).toEqual({ total: 9, finalizadas: 0, emProcessamento: 0, comErro: 0, salvas: 0 });
    expect(cardSums([nfe, nfse], '55')).toEqual({ total: 5, finalizadas: 0, emProcessamento: 0, comErro: 5, salvas: 0 });
  });

  it('o modelo que o período não tem dá zero', () => {
    expect(cardSums([nfe], 'SE')).toEqual({ total: 0, finalizadas: 0, emProcessamento: 0, comErro: 0, salvas: 0 });
  });

  it('o registro antigo, sem modelo, conta em todos', () => {
    expect(cardSums([nfe, { ...nfse, model: null }], null).total).toBe(14);
  });

  // A faixa nova (capture-without-dispatch, 8.1): a salva conta em "Documentos" e em "Salvas", e em mais nenhum card.
  it('somam as salvas, e elas ficam fora de finalizadas, em processamento e com erro', () => {
    const comSalvas: ModelTotals = { model: '55', total: 4, finalizadas: 1, emProcessamento: 0, comErro: 0, salvas: 3 };

    expect(cardSums([comSalvas], null)).toEqual({ total: 4, finalizadas: 1, emProcessamento: 0, comErro: 0, salvas: 3 });
    expect(cardSums([comSalvas, nfe], null).salvas).toBe(3);
    expect(cardSums([comSalvas, nfe], null).total).toBe(9);
  });
});

// O subtítulo do card (conferência de 2026-10-16): o da "Salvas" dizia "salvas sem enviar: envio desligado" — texto
// fixo afirmando um status ao vivo, que continuava na tela com a chave já religada. Os irmãos dele sempre foram
// definições de categoria, e agora os quatro são.
describe('os subtítulos dos cards', () => {
  it('o da "Salvas" define a categoria, e não o estado da chave', () => {
    expect(CARD_NOTES.salvas).toBe('gravadas sem enviar ao compliance');
  });

  it('nenhum deles afirma um estado ao vivo', () => {
    // A guarda contra a reincidência: um subtítulo não fala de ligado/desligado, porque o card não sabe o estado
    // presente da chave — ele conta notas que já pararam.
    for (const note of Object.values(CARD_NOTES)) {
      expect(note).not.toMatch(/desligad|ligad/i);
    }
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
