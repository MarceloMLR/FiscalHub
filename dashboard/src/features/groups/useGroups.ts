import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';

export function useGroups() {
  return useQuery({ queryKey: ['groups'], queryFn: api.groups, refetchInterval: 5000 });
}

export function useGroupDocuments(company?: string, branch?: string, date?: string) {
  return useQuery({
    queryKey: ['groupDocuments', company, branch, date],
    queryFn: () => api.groupDocuments(company!, branch!, date!),
    enabled: Boolean(company && branch && date),
    refetchInterval: 5000,
  });
}
