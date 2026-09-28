import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';

// A leitura do desfecho de uma nota, para a primeira vista do detalhe. Sem fotos, a API dá 404, e a tela cai no reason.
export function useReading(tenantId?: string, naturalKey?: string) {
  return useQuery({
    queryKey: ['reading', tenantId, naturalKey],
    queryFn: () => api.reading(tenantId!, naturalKey!),
    enabled: Boolean(tenantId && naturalKey),
    retry: false,
  });
}
