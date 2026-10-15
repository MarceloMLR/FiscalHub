// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders, testQueryClient } from '../../test/renderWithProviders';
import { useAuth } from './AuthContext';
import type { AuthUser } from '../../types';

// A **ligação** do `AuthContext`: aqui o `AuthProvider` é **o real** (design D5), com um filho-sonda que só lê o
// contexto e escreve o que ele diz na tela. Sem `App`, sem casca, sem MUI.
//
// O `fetch` é stubado, e **não** o módulo do cliente (design D3): a coisa sob teste é a cadeia inteira —
// `fetch` → `getJson` → `ApiError(status)` → `sessionFromCheckError` → `tokenStore`. Mockar o `api/client` cortaria
// justamente o pedaço onde o defeito de 2026-10-15 morava, e o teste passaria a provar o dublê. Com o `fetch` stubado,
// o `tokenStore` real grava no `localStorage` real do jsdom, e a asserção é sobre o token de verdade.
//
// A `resetSessionCache` já tem teste próprio (`sessionCache.test.ts`) — **quem não tinha era a chamada dela**.

const TOKEN_KEY = 'fiscalhub.token';
const TOKEN = 'token-guardado';

const USER: AuthUser = { email: 'ana@acme.com.br', name: 'Ana Lima', tenantId: 'acme', role: 'Admin' };

// A sonda: lê o contexto e expõe o `kind` e as três ações. Ela não decide nada — é só uma janela para o provider.
function Sonda() {
  const { session, login, logout, recheckSession } = useAuth();
  return (
    <div>
      <span data-testid="kind">{session.kind}</span>
      <button type="button" onClick={() => void login(USER.email, 'senha').catch(() => {})}>
        entrar
      </button>
      <button type="button" onClick={logout}>
        sair
      </button>
      <button type="button" onClick={recheckSession}>
        tentar de novo
      </button>
    </div>
  );
}

function kind(): string {
  return screen.getByTestId('kind').textContent ?? '';
}

function render(queryClient = testQueryClient()) {
  return renderWithProviders(<Sonda />, { queryClient, auth: true, theme: false });
}

function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, statusText: 'OK', json: async () => body } as unknown as Response;
}

function statusResponse(status: number, statusText: string): Response {
  return { ok: false, status, statusText, json: async () => ({}) } as unknown as Response;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('só o 401 apaga o token guardado', () => {
  // A tabela das quatro falhas da checagem. O 401 é o único caso em que o servidor **respondeu**, e respondeu que a
  // sessão não vale; todo o resto é "não consegui perguntar", e "não consegui perguntar" não é uma resposta.
  const FALHAS: { nome: string; resposta: () => Promise<Response>; esperado: string; tokenFica: boolean }[] = [
    {
      nome: '401 — a sessão realmente não vale',
      resposta: async () => statusResponse(401, 'Unauthorized'),
      esperado: 'anonymous',
      tokenFica: false,
    },
    {
      nome: '500 — o servidor não conseguiu responder',
      resposta: async () => statusResponse(500, 'Internal Server Error'),
      esperado: 'failed',
      tokenFica: true,
    },
    {
      nome: 'rede — a chamada nem saiu',
      resposta: () => Promise.reject(new TypeError('Failed to fetch')),
      esperado: 'failed',
      tokenFica: true,
    },
    {
      nome: 'timeout — a resposta não chegou',
      resposta: () => Promise.reject(Object.assign(new Error('timeout'), { name: 'TimeoutError' })),
      esperado: 'failed',
      tokenFica: true,
    },
  ];

  it.each(FALHAS)('$nome', async ({ resposta, esperado, tokenFica }) => {
    localStorage.setItem(TOKEN_KEY, TOKEN);
    vi.stubGlobal('fetch', vi.fn(resposta));

    render();

    await waitFor(() => expect(kind()).toBe(esperado));
    // A asserção é sobre o `localStorage` de verdade do jsdom, e não sobre um dublê do `tokenStore`.
    expect(localStorage.getItem(TOKEN_KEY)).toBe(tokenFica ? TOKEN : null);
  });

  it('uma indisponibilidade do servidor não destrói a sessão, e um 401 destrói', async () => {
    // O par que o defeito confundia, lado a lado: os dois são falha da mesma chamada, e os destinos são opostos.
    localStorage.setItem(TOKEN_KEY, TOKEN);
    vi.stubGlobal('fetch', vi.fn(async () => statusResponse(503, 'Service Unavailable')));
    render();
    await waitFor(() => expect(kind()).toBe('failed'));
    expect(localStorage.getItem(TOKEN_KEY)).toBe(TOKEN);

    cleanup();

    localStorage.setItem(TOKEN_KEY, TOKEN);
    vi.stubGlobal('fetch', vi.fn(async () => statusResponse(401, 'Unauthorized')));
    render();
    await waitFor(() => expect(kind()).toBe('anonymous'));
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
  });
});

describe('a troca de sessão limpa o cache de consultas', () => {
  // O defeito 1 de 2026-10-15 morava aqui: trocar de usuário sem recarregar deixava o `/info` do Admin anterior no
  // cache, e um Viewer continuava vendo os botões da visão técnica.
  function semearCache() {
    const qc = testQueryClient();
    qc.setQueryData(['info'], { canViewRawJson: true });
    qc.setQueryData(['groups', '2026-10-15', '2026-10-15', null], []);
    expect(qc.getQueryCache().getAll()).toHaveLength(2);
    return qc;
  }

  it('o login limpa o que estava guardado para o token anterior', async () => {
    const qc = semearCache();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ token: 'token-novo', expiresAt: '2026-10-16T00:00:00Z', user: USER })),
    );
    render(qc);
    await waitFor(() => expect(kind()).toBe('anonymous'));

    fireEvent.click(screen.getByRole('button', { name: 'entrar' }));

    await waitFor(() => expect(kind()).toBe('authenticated'));
    expect(qc.getQueryCache().getAll()).toHaveLength(0);
  });

  it('o logout limpa o que estava guardado para quem saiu', async () => {
    const qc = semearCache();
    localStorage.setItem(TOKEN_KEY, TOKEN);
    vi.stubGlobal('fetch', vi.fn(async () => jsonResponse(USER)));
    render(qc);
    await waitFor(() => expect(kind()).toBe('authenticated'));
    expect(qc.getQueryCache().getAll()).toHaveLength(2);

    fireEvent.click(screen.getByRole('button', { name: 'sair' }));

    await waitFor(() => expect(kind()).toBe('anonymous'));
    expect(qc.getQueryCache().getAll()).toHaveLength(0);
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
  });
});

describe('o "tentar de novo" re-executa a checagem', () => {
  it('a segunda resposta leva ao painel, sem passar pelo login', async () => {
    localStorage.setItem(TOKEN_KEY, TOKEN);
    const chamadas = vi
      .fn<() => Promise<Response>>()
      .mockResolvedValueOnce(statusResponse(503, 'Service Unavailable'))
      .mockResolvedValueOnce(jsonResponse(USER));
    vi.stubGlobal('fetch', chamadas);

    render();
    await waitFor(() => expect(kind()).toBe('failed'));

    fireEvent.click(screen.getByRole('button', { name: 'tentar de novo' }));

    await waitFor(() => expect(kind()).toBe('authenticated'));
    // A checagem foi **refeita**, e não um segundo mecanismo: a mesma chamada, duas vezes.
    expect(chamadas).toHaveBeenCalledTimes(2);
    // E o token nunca saiu: quem falhou foi o servidor, e não a sessão.
    expect(localStorage.getItem(TOKEN_KEY)).toBe(TOKEN);
  });
});
