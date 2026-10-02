import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { periodParam } from './groupRow';
import type { DocumentGroup } from '../../types';

// A tabela pelo mesmo filtro dos cards: a janela de dias da execução e o modelo (D15). Desligada com os cards, enquanto o
// período personalizado tem um problema.
export function useGroups(from: string, to: string, model: string | null, enabled = true) {
  return useQuery({ queryKey: ['groups', from, to, model], queryFn: () => api.groups(from, to, model), refetchInterval: 5000, enabled });
}

// As contagens dos cards por modelo na janela de dias da execução, no ritmo da tabela. Desligada enquanto o período
// personalizado tem um problema (uma data vazia, ou a inicial depois da final).
export function useGroupTotals(from: string, to: string, enabled = true) {
  return useQuery({ queryKey: ['groupTotals', from, to], queryFn: () => api.groupTotals(from, to), refetchInterval: 5000, enabled });
}

// As notas da linha inteira: empresa, filial, dia da execução, período, tipo, modelo e modo (o modal conta o mesmo que o
// título).
export function useGroupDocuments(group: DocumentGroup | null) {
  const period = group ? periodParam(group) : null;
  return useQuery({
    queryKey: ['groupDocuments', group?.companyCode, group?.branchCode, group?.executedOn, period, group?.type, group?.model, group?.trigger],
    queryFn: () => api.groupDocuments(group!, period!),
    enabled: group !== null,
    refetchInterval: 5000,
  });
}
