import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { DocumentTrace, TraceResponse } from '../../types';

// Categoriza o mapa de blobs do /trace nas tres fotos (fonte / dominio / destino).
function shape(res: TraceResponse): DocumentTrace {
  const trace: DocumentTrace = {};
  for (const [path, content] of Object.entries(res)) {
    if (path.endsWith('source.xml')) {
      trace.source = String(content);
    } else if (path.endsWith('domain.json')) {
      trace.domain = content;
    } else if (path.endsWith('.json')) {
      const name = path.split('/').pop()!.replace('.json', '');
      trace.destination = { name, payload: content };
    }
  }
  return trace;
}

export function useTrace(tenantId?: string, naturalKey?: string) {
  return useQuery({
    queryKey: ['trace', tenantId, naturalKey],
    queryFn: async () => shape(await api.trace(tenantId!, naturalKey!)),
    enabled: Boolean(tenantId && naturalKey),
  });
}
