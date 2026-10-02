import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { DocumentGroup } from '../../types';

export function useGroups() {
  return useQuery({ queryKey: ['groups'], queryFn: api.groups, refetchInterval: 5000 });
}

// As contagens dos cards por modelo na janela de dias fiscais, no ritmo da tabela.
export function useGroupTotals(from: string, to: string) {
  return useQuery({ queryKey: ['groupTotals', from, to], queryFn: () => api.groupTotals(from, to), refetchInterval: 5000 });
}

// As notas da linha inteira: empresa, filial, dia, tipo, modelo e modo (o modal conta o mesmo que o título).
export function useGroupDocuments(group: DocumentGroup | null) {
  return useQuery({
    queryKey: ['groupDocuments', group?.companyCode, group?.branchCode, group?.referenceDate, group?.type, group?.model, group?.trigger],
    queryFn: () => api.groupDocuments(group!),
    enabled: group !== null,
    refetchInterval: 5000,
  });
}
