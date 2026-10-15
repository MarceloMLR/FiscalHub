import type { InfoResponse } from '../types';

// O que a casca do dashboard mostra, decidido a partir do estado da consulta do /info (spec module-navigation, "Sem o
// /info, o dashboard não desenha o painel"; design D1 a D4).
//
// **Por que isto não é um ternário dentro do App.tsx.** Uma escolha se prova **caso a caso, de graça**, quando é um
// valor que entra e um valor que sai; dentro do componente ela só se prova montando a tela. A escolha mora aqui, onde
// o teste alcança; o componente só renderiza o que esta função disser. Daí ela não importar React nem React Query:
// recebe os sinais como **dados**.
//
// O dashboard **passou a ter** stack de teste de componente (jsdom + `@testing-library/react`, change
// `dashboard-component-test-stack`), e a ligação — a linha do `App.tsx` que chama isto e renderiza o que vier — tem
// teste próprio no `src/App.test.tsx`. Ganhar a ferramenta não é motivo para mover a escolha para dentro da tela: as
// duas coisas são testadas, cada uma onde é barata.
//
// **O nulo não é o vazio, e agora são três estados, e não dois.** "Ainda não respondeu" espera; "respondeu" desenha o
// painel, com a árvore que veio — vazia ou não; "falhou" vai para a tela de falha. Traduzir falha para árvore vazia é
// o defeito que esta função apaga: a barra lateral passava a afirmar ao cliente, por causa de um 5xx, que ele não tem
// conector configurado.

// Os sinais da consulta, e só eles: a resposta em mãos, se está buscando e se terminou em erro.
export interface InfoQueryState {
  info: InfoResponse | undefined;
  isFetching: boolean;
  isError: boolean;
}

export type ShellState =
  // Há resposta: o painel é desenhado a partir **dela**, e não de um `info?.` que ninguém sabe se chegou.
  | { kind: 'panel'; info: InfoResponse }
  | { kind: 'loading' }
  | { kind: 'failure' };

// A ordem é a decisão (design D4), e não acidente de escrita — trocá-la quebra o teste de tabela ao lado:
//
// 1. **resposta em mãos** vence tudo: uma recarga em curso, ou uma recarga que falhou, não derruba o painel que já
//    está de pé (D6). Ele fica velho, e velho não é mentira;
// 2. **está buscando** vem antes do erro: no React Query v5 o `status` continua `'error'` durante a nova tentativa, e
//    ler o erro primeiro mostraria a tela de falha justamente enquanto o "tentar de novo" tenta;
// 3. **terminou em erro**, sem resposta nenhuma: a tela de falha;
// 4. o resto — o primeiro render e a pausa por offline — espera.
export function shellState({ info, isFetching, isError }: InfoQueryState): ShellState {
  if (info !== undefined) return { kind: 'panel', info };
  if (isFetching) return { kind: 'loading' };
  if (isError) return { kind: 'failure' };
  return { kind: 'loading' };
}
