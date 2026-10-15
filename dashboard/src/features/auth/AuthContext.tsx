import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, tokenStore, setUnauthorizedHandler } from '../../api/client';
import { resetSessionCache } from './sessionCache';
import type { AuthUser } from '../../types';

interface AuthState {
  user: AuthUser | null;
  ready: boolean; // já tentou restaurar a sessão do token guardado
  login: (email: string, password: string, remember?: boolean) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [ready, setReady] = useState(false);
  const queryClient = useQueryClient();

  useEffect(() => {
    // Se a API responder 401 em qualquer chamada, cai pro login.
    setUnauthorizedHandler(() => setUser(null));

    // Restaura a sessão: com token guardado, valida em /auth/me.
    const token = tokenStore.get();
    if (!token) {
      setReady(true);
      return;
    }

    api
      .me()
      .then(setUser)
      .catch(() => {
        tokenStore.set(null);
        setUser(null);
      })
      .finally(() => setReady(true));

    return () => setUnauthorizedHandler(null);
  }, []);

  const login = async (email: string, password: string, remember = true) => {
    const res = await api.login(email, password);
    tokenStore.set(res.token, remember);
    // Antes de o usuário novo aparecer na tela: nada do que estava guardado foi respondido para este token. Limpar no
    // login também é o que cobre a queda por 401, que leva ao login sem passar pelo logout.
    resetSessionCache(queryClient);
    setUser(res.user);
  };

  const logout = () => {
    tokenStore.set(null);
    setUser(null);
    // Depois de o usuário sair da tela: limpar antes faria as consultas ainda montadas refazerem a busca sem token.
    resetSessionCache(queryClient);
  };

  return <AuthContext.Provider value={{ user, ready, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth deve estar dentro de AuthProvider');
  }
  return ctx;
}
