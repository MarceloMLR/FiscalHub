import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { DocumentTrace, TraceResponse } from '../../types';

// Categoriza o mapa de fotos do /trace (nome do arquivo → conteúdo). A ordem importa: a fonte pode ser JSON
// (a do D365) e as respostas terminam em .json como o payload — só o que sobra é o <destino>.json.
function shape(res: TraceResponse): DocumentTrace {
  const trace: DocumentTrace = {};
  for (const [path, content] of Object.entries(res)) {
    const file = path.split('/').pop()!;
    if (file.startsWith('source.')) {
      trace.source = typeof content === 'string' ? content : JSON.stringify(content, null, 2);
    } else if (file === 'domain.json') {
      trace.domain = content;
    } else if (file.endsWith('.response.submit.json')) {
      trace.responses = { ...trace.responses, submit: content };
    } else if (file.endsWith('.response.status.json')) {
      trace.responses = { ...trace.responses, status: content };
    } else if (file.endsWith('.json')) {
      trace.destination = { name: file.replace(/\.json$/, ''), payload: content };
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
