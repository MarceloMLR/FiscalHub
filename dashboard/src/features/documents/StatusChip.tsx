import Chip from '@mui/material/Chip';
import type { IntegrationStatus } from '../../types';

type ChipColor = 'default' | 'info' | 'success' | 'error' | 'warning';

const colors: Record<IntegrationStatus, ChipColor> = {
  Pending: 'default',
  Submitted: 'info',
  Confirmed: 'success',
  IntegrationError: 'error',
  Unconfirmed: 'warning',
  DeadLettered: 'default',
};

const labels: Record<IntegrationStatus, string> = {
  Pending: 'Pendente',
  Submitted: 'Em voo',
  Confirmed: 'Confirmado',
  IntegrationError: 'Rejeitado',
  Unconfirmed: 'Sem resposta',
  DeadLettered: 'Dead-letter',
};

export function StatusChip({ status }: { status: IntegrationStatus }) {
  return <Chip size="small" color={colors[status] ?? 'default'} label={labels[status] ?? status} />;
}
