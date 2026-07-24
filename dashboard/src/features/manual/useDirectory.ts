import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';

const FIVE_MIN = 5 * 60 * 1000;

// Diretorio muda pouco — cache generoso pra nao repuxar a cada abertura da tela.
export function useCompanies() {
  return useQuery({ queryKey: ['companies'], queryFn: api.companies, staleTime: FIVE_MIN });
}

export function useBranches(companyCode: string) {
  return useQuery({
    queryKey: ['branches', companyCode],
    queryFn: () => api.branches(companyCode),
    enabled: companyCode !== '',
    staleTime: FIVE_MIN,
  });
}
