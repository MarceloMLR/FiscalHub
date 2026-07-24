import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';

// Agendamentos e execuções mudam sozinhos (o timer do host roda) — repuxa a cada 5s.
export function useSchedules() {
  return useQuery({ queryKey: ['schedules'], queryFn: api.schedules, refetchInterval: 5000 });
}

export function useExecutions() {
  return useQuery({ queryKey: ['executions'], queryFn: api.executions, refetchInterval: 5000 });
}
