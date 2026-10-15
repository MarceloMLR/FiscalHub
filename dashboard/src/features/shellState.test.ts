import { describe, expect, it } from 'vitest';
import { shellState, type InfoQueryState, type ShellState } from './shellState';
import type { InfoResponse } from '../types';

// A escolha entre carregando, painel e falha (spec module-navigation, "Sem o /info, o dashboard não desenha o painel";
// design D2 e D4). O teste é de tabela porque o que esta fatia precisa travar é a **ordem** em que os sinais são lidos:
// no React Query v5 o `status` continua `'error'` durante a nova tentativa, e ler o erro cedo demais mostraria a tela
// de falha justamente enquanto o "tentar de novo" tenta.

const RESPOSTA: InfoResponse = {
  environment: 'Sandbox',
  automaticIntegration: true,
  inboundScans: true,
  connectors: [{ adapter: 'Avalara', label: 'Tax Compliance', scheduling: true, modules: ['Fiscal'] }],
  canViewRawJson: true,
};

// A resposta do tenant sem conector nenhum: árvore **vazia**, que é uma resposta — e é ela que nomeia a ausência.
const RESPOSTA_VAZIA: InfoResponse = { ...RESPOSTA, connectors: [] };

const CASOS: { nome: string; entrada: InfoQueryState; esperado: ShellState }[] = [
  {
    // O primeiro render, antes de a consulta sair, e a pausa por offline (`fetchStatus` `'paused'`): não se sabe nada,
    // e não saber é esperar — nunca afirmar.
    nome: 'ainda não respondeu, e nem está buscando: carregando',
    entrada: { info: undefined, isFetching: false, isError: false },
    esperado: { kind: 'loading' },
  },
  {
    nome: 'ainda não respondeu, buscando pela primeira vez: carregando',
    entrada: { info: undefined, isFetching: true, isError: false },
    esperado: { kind: 'loading' },
  },
  {
    nome: 'falhou, sem resposta nenhuma em mãos: a tela de falha',
    entrada: { info: undefined, isFetching: false, isError: true },
    esperado: { kind: 'failure' },
  },
  {
    // O "tentar de novo" em curso. O erro **continua** verdadeiro aqui (React Query v5): quem ler o erro antes de ler o
    // "está buscando" mostra falha enquanto a tela tenta, que é o que o requisito proíbe.
    nome: 'buscando depois de um erro — o tentar de novo: carregando, e não falha',
    entrada: { info: undefined, isFetching: true, isError: true },
    esperado: { kind: 'loading' },
  },
  {
    nome: 'respondeu com a árvore do tenant: o painel',
    entrada: { info: RESPOSTA, isFetching: false, isError: false },
    esperado: { kind: 'panel', info: RESPOSTA },
  },
  {
    // "Respondeu vazio" é resposta, e o painel é desenhado: a barra lateral mostra o grupo que nomeia a ausência.
    nome: 'respondeu com a árvore vazia: o painel',
    entrada: { info: RESPOSTA_VAZIA, isFetching: false, isError: false },
    esperado: { kind: 'panel', info: RESPOSTA_VAZIA },
  },
  {
    // Uma recarga em curso (a invalidação do /info ao salvar em Configurações) não tira o painel do ar.
    nome: 'recarga em curso, com resposta em mãos: o painel',
    entrada: { info: RESPOSTA, isFetching: true, isError: false },
    esperado: { kind: 'panel', info: RESPOSTA },
  },
  {
    // D6: a falha de uma **recarga** não derruba o painel. Ele fica velho, e velho não é mentira.
    nome: 'recarga falhou, com resposta em mãos: o painel continua de pé',
    entrada: { info: RESPOSTA, isFetching: false, isError: true },
    esperado: { kind: 'panel', info: RESPOSTA },
  },
];

describe('shellState', () => {
  for (const caso of CASOS) {
    it(caso.nome, () => {
      expect(shellState(caso.entrada)).toEqual(caso.esperado);
    });
  }

  // Os três estados do requisito, um a um, no mesmo teste: é a frase da spec virando asserção.
  it('os três estados são três, e não dois', () => {
    const aindaNaoRespondeu = shellState({ info: undefined, isFetching: true, isError: false });
    const respondeu = shellState({ info: RESPOSTA_VAZIA, isFetching: false, isError: false });
    const falhou = shellState({ info: undefined, isFetching: false, isError: true });

    expect(aindaNaoRespondeu.kind).toBe('loading');
    expect(respondeu.kind).toBe('panel');
    expect(falhou.kind).toBe('failure');
  });

  // O defeito que esta change existe para apagar: hoje a falha é traduzida para árvore vazia, e a barra lateral afirma
  // ao cliente que ele não tem conector configurado. "Falhou" e "respondeu vazio" não podem dar no mesmo lugar.
  it('"falhou" e "respondeu vazio" não dão no mesmo resultado', () => {
    const falhou = shellState({ info: undefined, isFetching: false, isError: true });
    const respondeuVazio = shellState({ info: RESPOSTA_VAZIA, isFetching: false, isError: false });

    expect(falhou).not.toEqual(respondeuVazio);
    expect(falhou.kind).not.toBe(respondeuVazio.kind);
  });

  // Havendo resposta, ela sai junto: o painel não volta a perguntar ao `undefined` o que mostrar.
  it('o painel carrega a resposta que o desenha', () => {
    const estado = shellState({ info: RESPOSTA, isFetching: false, isError: false });

    expect(estado.kind === 'panel' && estado.info).toBe(RESPOSTA);
  });
});
