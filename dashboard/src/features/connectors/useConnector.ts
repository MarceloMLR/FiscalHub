import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';

export function useConnector() {
  return useQuery({ queryKey: ['connector'], queryFn: api.connector });
}
