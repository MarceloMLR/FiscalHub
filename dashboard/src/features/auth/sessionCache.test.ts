import { describe, expect, it } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { resetSessionCache } from './sessionCache';
import { canViewRawJson } from '../rawJson';
import { navigationGroups } from '../modules/modules';
import type { InfoResponse } from '../../types';

// Trocar de usuário sem recarregar a página não pode deixar a tela decidir por dado do usuário anterior (achado na
// prova manual de 6.4: o Viewer continuou com os dois botões da visão técnica depois de um Admin ter logado antes).
// O cache do React Query é um singleton do main.tsx e vive enquanto a página viver; o /info é guardado com
// staleTime: Infinity, então ele nunca envelhece sozinho. Quem troca a sessão é quem tem que limpar.
describe('resetSessionCache', () => {
  // O mesmo staleTime do useInfo: o teste prova que a limpeza não depende de o dado estar velho.
  const clientWithFrozenCache = () => new QueryClient({ defaultOptions: { queries: { staleTime: Infinity } } });

  const info = (canViewRawJson: boolean, label: string): InfoResponse => ({
    environment: 'Sandbox',
    automaticIntegration: false,
    inboundScans: false,
    connectors: [{ adapter: 'Avalara', label, scheduling: true, modules: ['Fiscal'] }],
    canViewRawJson,
    outboundDispatch: true,
  });

  it('o /info do usuário anterior não decide os botões da visão técnica do próximo', () => {
    const qc = clientWithFrozenCache();
    qc.setQueryData(['info'], info(true, 'Tax Compliance'));
    expect(canViewRawJson(qc.getQueryData<InfoResponse>(['info']))).toBe(true);

    resetSessionCache(qc);

    // Sem resposta guardada, a tela volta a não afirmar nada até o /info do usuário novo responder — que é o que o
    // rawJson.ts já faz com undefined. O Viewer não herda a permissão do Admin que logou antes.
    expect(qc.getQueryData(['info'])).toBeUndefined();
    expect(canViewRawJson(qc.getQueryData<InfoResponse>(['info']))).toBe(false);
  });

  it('a árvore de navegação do tenant anterior não sobrevive à troca', () => {
    const qc = clientWithFrozenCache();
    qc.setQueryData(['info'], info(true, 'Tax Compliance'));

    resetSessionCache(qc);

    // Sem árvore, a barra lateral mostra o Carregando… da 4.8 — e não os conectores do tenant de quem saiu.
    const guardado = qc.getQueryData<InfoResponse>(['info']);
    expect(navigationGroups(guardado?.connectors)).toEqual([]);
  });

  it('não sobra consulta nenhuma do usuário anterior, e não só o /info', () => {
    const qc = clientWithFrozenCache();
    qc.setQueryData(['info'], info(true, 'Tax Compliance'));
    qc.setQueryData(['connector'], { modules: ['Fiscal'] });
    qc.setQueryData(['groups', 'tenant-a', '2026-10'], [{ id: 'g1' }]);
    expect(qc.getQueryCache().getAll()).toHaveLength(3);

    resetSessionCache(qc);

    // A limpeza é da sessão inteira: qualquer resposta escopada pelo token anterior sai, inclusive as de chave
    // composta. Enumerar as chaves aqui deixaria a próxima consulta nova vazando por esquecimento.
    expect(qc.getQueryCache().getAll()).toEqual([]);
    expect(qc.getQueryData(['connector'])).toBeUndefined();
    expect(qc.getQueryData(['groups', 'tenant-a', '2026-10'])).toBeUndefined();
  });
});
