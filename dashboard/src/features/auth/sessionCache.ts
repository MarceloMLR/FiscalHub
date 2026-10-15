import type { QueryClient } from '@tanstack/react-query';

// A troca de sessão limpa o cache de consultas. O cache do React Query é um singleton criado no `main.tsx`, que vive
// enquanto a página viver; nenhuma resposta guardada por ele é do usuário — ela é do token que estava valendo quando a
// consulta foi feita. Sem esta limpeza, trocar de usuário sem recarregar a página deixa a tela decidir pelo dado de
// quem saiu: o `/info` do Admin anterior mantinha os botões da visão técnica acesos para um Viewer, e o `/info` de
// outro tenant desenharia a árvore de conectores do tenant anterior na barra lateral.
//
// Não é vazamento de dado — o servidor continua barrando (403 no `/trace`, escopo por tenant). É afirmação de tela
// errada, e a tela é o que esta fatia passou a decidir pela resposta do servidor.
//
// É `clear()`, e não uma lista de chaves: enumerar o que sai deixaria a próxima consulta nova vazando por
// esquecimento. A limpeza não substitui as invalidações que já existem (salvar em Configurações invalida o `['info']`
// para a barra lateral se redesenhar) — aquelas são de dado que mudou dentro da mesma sessão, esta é de sessão que
// mudou.
export function resetSessionCache(queryClient: QueryClient): void {
  queryClient.clear();
}
