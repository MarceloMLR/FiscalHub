import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, tokenStore, setUnauthorizedHandler } from '../../api/client';
import type { AuthUser } from '../../types';

interface AuthState {
  user: AuthUser | null;
  ready: boolean; // já tentou restaurar a sessão do token guardado
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [ready, setReady] = useState(false);

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

  const login = async (email: string, password: string) => {
    const res = await api.login(email, password);
    tokenStore.set(res.token);
    setUser(res.user);
  };

  const logout = () => {
    tokenStore.set(null);
    setUser(null);
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
