// @vitest-environment jsdom
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, screen } from '@testing-library/react';
import { renderWithProviders, testQueryClient } from './test/renderWithProviders';
import type { SessionState } from './features/auth/sessionState';
import type { AuthUser, InfoResponse } from './types';

// A **ligação** do `App.tsx`, e não as escolhas: as funções puras `sessionState` e `shellState` já têm os testes
// delas, caso a caso. O que ficou sem teste quatro vezes — e está anotado nas tarefas 1.6, 4.0, 4.8 e 4.10 da change
// `connector-navigation-and-admin-json` — é a linha que *chama* a escolha e renderiza o que ela disser.
//
// Aqui o `useAuth` é **dublê** (design D5): o que está sob teste é "dado o `session.kind`, qual tela", que são quatro
// ramos síncronos. Dirigir isso pelo `AuthProvider` de verdade seria encenar quatro cenários de rede para afirmar
// quatro linhas. O provider real é testado no `features/auth/AuthContext.test.tsx`, que é a outra ligação.
//
// **Custo aceito, dito com todas as letras:** nenhum dos dois arquivos prova que o `App` está ligado ao
// `AuthProvider` de verdade — isso é uma linha do `main.tsx`, e ela continua sem teste (P2 do `tasks.md`).

const auth = vi.hoisted(() => ({
  session: { kind: 'checking' } as SessionState,
  login: vi.fn(),
  logout: vi.fn(),
  recheckSession: vi.fn(),
}));

vi.mock('./features/auth/AuthContext', () => ({
  useAuth: () => auth,
  // A mesma regra do real: lá dentro **há** usuário, e perguntar fora do painel é erro.
  useAuthUser: () => {
    if (auth.session.kind !== 'authenticated') {
      throw new Error('useAuthUser só vale dentro do painel, com sessão autenticada');
    }
    return auth.session.user;
  },
  AuthProvider: ({ children }: { children: ReactNode }) => children,
}));

// O import vem **depois** do `vi.mock` na leitura, mas o vitest iça a chamada acima dele: o `App` já nasce com o
// `useAuth` dublê.
import App from './App';

const USER: AuthUser = { email: 'ana@acme.com.br', name: 'Ana Lima', tenantId: 'acme', role: 'Admin' };

const INFO: InfoResponse = {
  environment: 'Production',
  automaticIntegration: true,
  inboundScans: true,
  connectors: [{ adapter: 'D365', label: 'Dynamics 365 F&O', scheduling: true, modules: ['Fiscal'] }],
  canViewRawJson: true,
};

// As telas, pelo texto que cada uma mostra e nenhuma outra mostra.
const CARREGANDO = 'Carregando…';
const FALHA = 'Não foi possível carregar o painel.';
const LOGIN = 'Entrar no painel';

// O painel se reconhece pelo tenant da barra lateral e pelo rótulo de um item de navegação — e **não** pelo conteúdo
// da tabela, que é do `GroupsPage` e não desta change (design D6).
function painelDesenhado(): boolean {
  return screen.queryByText(USER.tenantId) !== null && screen.queryAllByText('Fiscal').length > 0;
}

// Uma resposta JSON qualquer, para as consultas do painel (o `GroupsPage`) não ficarem pendentes.
function jsonResponse(body: unknown): Response {
  return { ok: true, status: 200, statusText: 'OK', json: async () => body } as unknown as Response;
}

function renderApp(session: SessionState, queryClient = testQueryClient()) {
  auth.session = session;
  return renderWithProviders(<App />, { queryClient });
}

beforeEach(() => {
  // O painel autenticado monta o `GroupsPage`: sem isto, as consultas dele ficariam pendentes e o teste penduraria.
  vi.stubGlobal('fetch', vi.fn(async () => jsonResponse([])));
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
  auth.session = { kind: 'checking' };
});

describe('o portão de sessão escolhe a tela', () => {
  // A tabela dos quatro ramos. O `authenticated` chega com o `/info` **semeado**, porque sem resposta ele cairia no
  // carregando da casca — que é o ramo do outro bloco, e não deste.
  const RAMOS: { nome: string; session: SessionState; esperado: string }[] = [
    { nome: 'checando', session: { kind: 'checking' }, esperado: CARREGANDO },
    { nome: 'falhou', session: { kind: 'failed' }, esperado: FALHA },
    { nome: 'anônimo', session: { kind: 'anonymous' }, esperado: LOGIN },
    { nome: 'autenticado', session: { kind: 'authenticated', user: USER }, esperado: 'painel' },
  ];

  it.each(RAMOS)('$nome leva à tela certa', ({ session, esperado }) => {
    const qc = testQueryClient();
    qc.setQueryData(['info'], INFO);
    renderApp(session, qc);

    if (esperado === 'painel') {
      expect(painelDesenhado()).toBe(true);
    } else {
      expect(screen.getByText(esperado)).toBeTruthy();
      expect(painelDesenhado()).toBe(false);
    }
  });

  it('falhou não é anônimo: a tela é a de falha, e não a de login', () => {
    renderApp({ kind: 'failed' });

    expect(screen.getByText(FALHA)).toBeTruthy();
    expect(screen.queryByText(LOGIN)).toBeNull();
    // A tela de falha tem as duas saídas, e nenhuma delas é "entrar de novo".
    expect(screen.getByRole('button', { name: /Tentar de novo/ })).toBeTruthy();
    expect(screen.getByRole('button', { name: /Sair/ })).toBeTruthy();
  });

  it('o "tentar de novo" da tela de falha da sessão aciona a re-checagem', () => {
    renderApp({ kind: 'failed' });

    fireEvent.click(screen.getByRole('button', { name: /Tentar de novo/ }));

    // A ligação é esta: o botão da tela de falha do **portão** refaz a checagem, e não o `/info` (D15). O que a
    // re-checagem faz por dentro é do `AuthContext`, e é o `AuthContext.test.tsx` que prova.
    expect(auth.recheckSession).toHaveBeenCalledTimes(1);
  });
});

describe('a casca renderiza o que o shellState disser', () => {
  // Daqui para baixo a sessão é sempre a autenticada: o que varia é o estado da consulta do `/info`.
  const AUTENTICADO: SessionState = { kind: 'authenticated', user: USER };

  it('sem resposta e buscando leva ao carregando', () => {
    // Uma promessa que nunca resolve: a consulta fica pendente, que é o estado "ainda não respondeu".
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
    renderApp(AUTENTICADO);

    expect(screen.getByText(CARREGANDO)).toBeTruthy();
    expect(painelDesenhado()).toBe(false);
  });

  it('respondido leva ao painel, desenhado a partir da resposta', () => {
    const qc = testQueryClient();
    qc.setQueryData(['info'], INFO);
    renderApp(AUTENTICADO, qc);

    expect(painelDesenhado()).toBe(true);
    // A barra lateral desenha o conector que veio na resposta, e não um inventado.
    expect(screen.getByText('Dynamics 365 F&O')).toBeTruthy();
    expect(screen.queryByText(FALHA)).toBeNull();
  });

  it('em erro, sem resposta nenhuma, leva à tela de falha', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: false, status: 503, statusText: 'Service Unavailable' }) as unknown as Response),
    );
    renderApp(AUTENTICADO);

    expect(await screen.findByText(FALHA)).toBeTruthy();
    expect(painelDesenhado()).toBe(false);
    // A mensagem não afirma nada sobre a configuração do cliente — em particular, não nomeia a ausência de conector.
    expect(screen.queryByText('Sem conector configurado')).toBeNull();
  });
});
