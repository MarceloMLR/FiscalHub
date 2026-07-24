import Chip from '@mui/material/Chip';
import type { IntegrationStatus } from '../../types';

// Status na língua do usuário fiscal (não "em voo"): pill suave, fundo tingido + texto na cor.
const map: Record<IntegrationStatus, { label: string; bg: string; fg: string }> = {
  Pending: { label: 'Pendente', bg: '#eef1f4', fg: '#5b6472' },
  Submitted: { label: 'Processando', bg: '#e6f0fd', fg: '#1d4ed8' },
  Confirmed: { label: 'Finalizado', bg: '#e7f6ec', fg: '#15803d' },
  IntegrationError: { label: 'Rejeitado', bg: '#fdeaea', fg: '#c81e1e' },
  Unconfirmed: { label: 'Sem retorno', bg: '#fdf2e3', fg: '#b45309' },
  DeadLettered: { label: 'Falha', bg: '#eceef1', fg: '#3f4754' },
};

export function StatusChip({ status }: { status: IntegrationStatus }) {
  const s = map[status] ?? { label: status, bg: '#eef1f4', fg: '#5b6472' };
  return (
    <Chip
      size="small"
      label={s.label}
      sx={{ bgcolor: s.bg, color: s.fg, borderRadius: '20px', height: 24 }}
    />
  );
}
