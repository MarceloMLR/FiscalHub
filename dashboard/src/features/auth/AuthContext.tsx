import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, tokenStore, setUnauthorizedHandler } from '../../api/client';
import { resetSessionCache } from './sessionCache';
import { sessionFromCheckError, type SessionState } from './sessionState';
import type { AuthUser } from '../../types';

interface AuthState {
  // Um estado com nome, e não um booleano "já tentei": checando, autenticado, anônimo ou falhou (design D14). O
  // portão do App.tsx renderiza o que ele disser.
  session: SessionState;
  login: (email: string, password: string, remember?: boolean) => Promise<void>;
  logout: () => void;
  // Refaz a checagem da sessão. Existe porque o `api.me()` **não** é consulta do React Query — é uma promise solta
  // num efeito —, então não há retry automático nem `refetch` para o "tentar de novo" chamar (D15).
  recheckSession: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<SessionState>({ kind: 'checking' });
  // O número da tentativa: a única coisa que o "tentar de novo" mexe. O efeito depende dele, então incrementá-lo
  // re-executa a checagem — um mecanismo só, e não um caminho paralelo que possa divergir do efeito (D15).
  const [attempt, setAttempt] = useState(0);
  const queryClient = useQueryClient();

  // Se a API responder 401 em qualquer chamada, depois de logado, cai pro login. Inalterado nesta fatia: o cliente já
  // apagou o token quando chega aqui.
  useEffect(() => {
    setUnauthorizedHandler(() => setSession({ kind: 'anonymous' }));
    return () => setUnauthorizedHandler(null);
  }, []);

  // Restaura a sessão: com token guardado, valida em /auth/me.
  useEffect(() => {
    const token = tokenStore.get();
    if (!token) {
      setSession({ kind: 'anonymous' });
      return;
    }

    let cancelled = false;
    setSession({ kind: 'checking' });

    api.me().then(
      (user) => {
        if (!cancelled) setSession({ kind: 'authenticated', user });
      },
      (error: unknown) => {
        if (cancelled) return;
        // Aqui morava o defeito: um `catch` genérico apagava o token em **qualquer** falha, então uma API fora
        // deslogava o usuário e o dashboard mostrava o login — nunca a tela de falha. Quem decide agora é a função
        // pura, testada caso a caso: só o 401 derruba a sessão.
        const next = sessionFromCheckError(error);
        if (next.kind === 'anonymous') {
          tokenStore.set(null);
        }
        setSession(next);
      },
    );

    return () => {
      cancelled = true;
    };
  }, [attempt]);

  const login = async (email: string, password: string, remember = true) => {
    const res = await api.login(email, password);
    tokenStore.set(res.token, remember);
    // Antes de o usuário novo aparecer na tela: nada do que estava guardado foi respondido para este token. Limpar no
    // login também é o que cobre a queda por 401, que leva ao login sem passar pelo logout.
    resetSessionCache(queryClient);
    setSession({ kind: 'authenticated', user: res.user });
  };

  const logout = () => {
    tokenStore.set(null);
    setSession({ kind: 'anonymous' });
    // Depois de o usuário sair da tela: limpar antes faria as consultas ainda montadas refazerem a busca sem token.
    resetSessionCache(queryClient);
  };

  const recheckSession = () => setAttempt((n) => n + 1);

  return (
    <AuthContext.Provider value={{ session, login, logout, recheckSession }}>{children}</AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth deve estar dentro de AuthProvider');
  }
  return ctx;
}

// O usuário de dentro do painel. O painel só é desenhado no estado autenticado, então ali **há** usuário: quem está
// lá dentro não precisa perguntar a um `user?.` se ele chegou — é o mesmo movimento do `shellState`, que entrega a
// resposta junto do `panel`. Fora do painel, quem decide é o `session`, que distingue anônimo de falhou.
export function useAuthUser(): AuthUser {
  const { session } = useAuth();
  if (session.kind !== 'authenticated') {
    throw new Error('useAuthUser só vale dentro do painel, com sessão autenticada');
  }
  return session.user;
}
