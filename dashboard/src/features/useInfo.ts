import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';

// O /info é o contrato que diz o ambiente da instância, o que o tenant tem e o que o usuário logado pode ver. A casca
// decide o que mostrar a partir de três sinais — a resposta em mãos, se ainda está buscando e se terminou em erro —, e
// a quarta coisa que ela precisa é a ação de tentar de novo, que é o próprio `refetch` da consulta: o mesmo caminho da
// invalidação, e não um segundo mecanismo (design D5). Quem escolhe entre os três estados é o `shellState`, e não isto
// aqui nem o componente.
//
// Sai daqui **só** o que os leitores usam: o resto do objeto do React Query não vaza para a tela. O `data` mantém o
// nome porque os outros leitores do /info (o detalhe do documento, o diálogo da nota e Configurações) leem só ele, e
// vivem dentro da casca — sem painel, eles não chegam a existir.
//
// O `staleTime: Infinity` e a `queryKey` ficam como estão: a invalidação ao salvar em Configurações e a limpeza de
// cache na troca de sessão dependem deles.
export function useInfo() {
  const { data, isFetching, isError, refetch } = useQuery({
    queryKey: ['info'],
    queryFn: api.info,
    staleTime: Infinity,
  });
  return { data, isFetching, isError, refetch };
}
