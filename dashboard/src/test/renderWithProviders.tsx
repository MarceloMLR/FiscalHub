import type { ReactElement } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import { AuthProvider } from '../features/auth/AuthContext';
import { ThemeModeProvider } from '../theme/ThemeModeProvider';

// A árvore de providers que os testes de componente precisam (design D7). Ela **duplica** a do `main.tsx`
// (`QueryClientProvider → ThemeModeProvider → AuthProvider → App`), de propósito: extrair a árvore do `main.tsx` para
// um componente reutilizável seria refatorar produção para servir o teste, que é não-objetivo declarado. A duplicação
// é pequena, visível, e quando faltar um provider o teste quebra ruidosamente — que é o modo bom de descobrir.
//
// **Sem `StrictMode`** (design D8): o `main.tsx` usa, mas sob ele o efeito de restauração de sessão roda duas vezes, o
// que duplicaria as respostas que o `fetch` stubado precisa dar e faria a asserção depender de ordem de montagem.
//
// Este arquivo vive em `src/` porque o `tsconfig.json` só inclui `src` — ele é type-checked pelo `npm run build` como
// qualquer outro. **Nada de produção o importa**, então ele não entra no bundle.

// O `QueryClient` de teste nasce **sem retry**: o padrão do React Query repete 3 vezes com backoff exponencial, e um
// teste de falha com o cliente padrão esperaria segundos e ficaria instável (design D3). Isto é configuração **do
// teste** — nada em produção muda, e o `useInfo` não ganha `retry`.
export function testQueryClient(): QueryClient {
  return new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
}

export interface ProviderOptions {
  // Um cliente próprio, quando o teste precisa semear antes de renderizar. Sem isto, nasce um novo a cada chamada.
  queryClient?: QueryClient;
  // O tema (MUI + tokens). Ligado por padrão: a casca e as telas do painel o usam.
  theme?: boolean;
  // O `AuthProvider` **de verdade**. Desligado por padrão: o teste do `App` dubla o `useAuth` (design D5), e só o
  // teste do próprio `AuthContext` monta o provider real.
  auth?: boolean;
}

export interface ProviderRender extends RenderResult {
  queryClient: QueryClient;
}

export function renderWithProviders(ui: ReactElement, options: ProviderOptions = {}): ProviderRender {
  const queryClient = options.queryClient ?? testQueryClient();
  const withAuth = options.auth === true ? <AuthProvider>{ui}</AuthProvider> : ui;
  const tree = options.theme === false ? withAuth : <ThemeModeProvider>{withAuth}</ThemeModeProvider>;
  return {
    ...render(<QueryClientProvider client={queryClient}>{tree}</QueryClientProvider>),
    queryClient,
  };
}
