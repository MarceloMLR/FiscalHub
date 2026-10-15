import { ApiError } from '../../api/ApiError';
import type { AuthUser } from '../../types';

// O estado do portão de sessão, e a classificação do erro da checagem (spec module-navigation, "Sem saber quem é o
// usuário e o que o tenant tem, o dashboard não desenha o painel"; design D12 a D15).
//
// **Por que isto não é um `catch` dentro do componente.** É a mesma razão do `shellState`: o dashboard não tem stack
// de teste de componente (sem `@testing-library/react`, sem jsdom), e a linha que *escolhe* ficaria sem teste. A
// escolha mora aqui, onde o teste alcança, e por isso este módulo não importa React.

// São **quatro** situações, e não um booleano "já tentei" (D14). Duas delas — anônimo e falhou — são exatamente as
// que o defeito confundia: o portão apagava o token em qualquer falha do `/auth/me` e mandava o usuário para o login,
// então uma API fora virava "a sua sessão não vale".
export type SessionState =
  // Ainda perguntando quem é o usuário: nada a afirmar, nem a sessão nem a falta dela.
  | { kind: 'checking' }
  // Há usuário, e o painel é desenhado a partir **dele** — lá dentro não existe usuário nulo.
  | { kind: 'authenticated'; user: AuthUser }
  // Não há sessão: sem token guardado, por 401, ou porque o usuário saiu. É a tela de login.
  | { kind: 'anonymous' }
  // Não deu para perguntar. O token **continua guardado**, e a tela é a de falha da casca.
  | { kind: 'failed' };

// Os dois destinos possíveis de uma checagem que não deu certo.
export type SessionCheckFailure = Extract<SessionState, { kind: 'anonymous' } | { kind: 'failed' }>;

// Dado o erro do `/auth/me`, para onde a sessão vai.
//
// **Só o 401 derruba.** Ele é o único caso em que o servidor *respondeu*, e respondeu que a sessão não vale — aí
// apagar o token e ir ao login é a verdade. Todo o resto — 5xx, rede, timeout, erro sem status — é "não consegui
// perguntar", e "não consegui perguntar" não é uma resposta: mantém o token e vai para a tela de falha, com a ação de
// tentar de novo.
//
// A dúvida cai para o lado seguro (S8): erro que não diz 401 preserva a sessão. O custo de errar para esse lado é uma
// tela de falha a mais; o de errar para o outro é destruir a sessão de quem não pediu para sair. E quem de fato
// perdeu a sessão cai no primeiro 401 que o servidor conseguir responder.
export function sessionFromCheckError(error: unknown): SessionCheckFailure {
  if (error instanceof ApiError && error.status === 401) {
    return { kind: 'anonymous' };
  }
  return { kind: 'failed' };
}
