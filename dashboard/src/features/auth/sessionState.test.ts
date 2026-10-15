import { describe, expect, it } from 'vitest';
import { sessionFromCheckError, type SessionCheckFailure } from './sessionState';
import { ApiError } from '../../api/ApiError';

// A classificação do erro da checagem de sessão (spec module-navigation, "Sem saber quem é o usuário e o que o tenant
// tem, o dashboard não desenha o painel"; design D12 e D13).
//
// O que este teste trava: **só o 401 derruba a sessão.** Antes desta fatia o `catch` da restauração era genérico —
// 401, 500, rede e timeout caíam todos nele, e todos apagavam o token. Com a API fora, o dashboard não mostrava a
// tela de falha: mostrava o **login**, e o token guardado sumia. Uma indisponibilidade do servidor destruía a sessão
// do usuário.

// Os erros que o `api.me()` rejeita de verdade: o `getJson` lança `ApiError` com o status, e `fetch` rejeita com
// `TypeError` quando a rede não vai.
const REDE = new TypeError('Failed to fetch');
const TIMEOUT = new DOMException('The operation was aborted.', 'AbortError');

const CASOS: { nome: string; erro: unknown; esperado: SessionCheckFailure }[] = [
  {
    // A única resposta que diz, de fato, que a sessão não vale: o servidor respondeu, e respondeu que não.
    nome: '401: a sessão realmente não vale — anônimo',
    erro: new ApiError(401, 'Sessão expirada.'),
    esperado: { kind: 'anonymous' },
  },
  {
    nome: '500: o servidor quebrou, e não disse nada sobre a sessão — falhou',
    erro: new ApiError(500, '500 Internal Server Error'),
    esperado: { kind: 'failed' },
  },
  {
    nome: '502: o servidor nem chegou a atender — falhou',
    erro: new ApiError(502, '502 Bad Gateway'),
    esperado: { kind: 'failed' },
  },
  {
    nome: '503: fora do ar — falhou',
    erro: new ApiError(503, '503 Service Unavailable'),
    esperado: { kind: 'failed' },
  },
  {
    // 403 é resposta, mas não é "a sua sessão não vale": é "você não pode isto". Derrubar a sessão por causa dele
    // seria de novo responder uma pergunta que ninguém fez.
    nome: '403: respondeu, mas não disse que a sessão caiu — falhou',
    erro: new ApiError(403, '403 Forbidden'),
    esperado: { kind: 'failed' },
  },
  {
    nome: 'rede fora: nem houve resposta — falhou',
    erro: REDE,
    esperado: { kind: 'failed' },
  },
  {
    nome: 'timeout: a pergunta não voltou — falhou',
    erro: TIMEOUT,
    esperado: { kind: 'failed' },
  },
  {
    // Erro sem status nenhum. Na dúvida, o lado seguro é **manter** o token: quem não sabe se a sessão vale não pode
    // destruí-la (design S8).
    nome: 'erro sem status: na dúvida, mantém a sessão — falhou',
    erro: new Error('qualquer coisa'),
    esperado: { kind: 'failed' },
  },
  {
    // Só o erro do cliente de API diz 401. Um objeto qualquer com `status: 401` não é resposta do servidor, e a
    // classificação não adivinha — de novo para o lado seguro.
    nome: 'objeto qualquer com status 401, que não veio do cliente de API — falhou',
    erro: { status: 401 },
    esperado: { kind: 'failed' },
  },
  {
    nome: 'nada: a rejeição sem erro — falhou',
    erro: undefined,
    esperado: { kind: 'failed' },
  },
];

describe('sessionFromCheckError', () => {
  for (const caso of CASOS) {
    it(caso.nome, () => {
      expect(sessionFromCheckError(caso.erro)).toEqual(caso.esperado);
    });
  }

  // O defeito que o achado da prova manual mostrou, virando teste: o `catch` genérico dava o mesmo destino aos dois.
  it('"401" e "5xx" não dão no mesmo resultado', () => {
    const naoVale = sessionFromCheckError(new ApiError(401, 'Sessão expirada.'));
    const naoConsegui = sessionFromCheckError(new ApiError(500, '500 Internal Server Error'));

    expect(naoVale).not.toEqual(naoConsegui);
    expect(naoVale.kind).not.toBe(naoConsegui.kind);
  });

  // A regra dita de uma vez: nada além do 401 pode levar ao login, porque é o login que apaga o token.
  it('nenhuma falha que não seja 401 leva a anônimo', () => {
    const naoSao401: unknown[] = [
      new ApiError(500, '500'),
      new ApiError(502, '502'),
      new ApiError(503, '503'),
      new ApiError(403, '403'),
      REDE,
      TIMEOUT,
      new Error('qualquer coisa'),
      undefined,
    ];

    for (const erro of naoSao401) {
      expect(sessionFromCheckError(erro).kind).toBe('failed');
    }
  });
});
