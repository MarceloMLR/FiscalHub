import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';

// Auto-refresh: o status muda sozinho (o poll confirma), então re-busca a cada 5s.
export function useDocuments() {
  return useQuery({
    queryKey: ['documents'],
    queryFn: api.documents,
    refetchInterval: 5000,
  });
}
