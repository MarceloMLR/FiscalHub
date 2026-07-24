import Chip from '@mui/material/Chip';
import type { DocumentGroup } from '../../types';

// Status agregado do grupo, derivado das contagens.
export function groupStatus(g: DocumentGroup): { label: string; bg: string; fg: string } {
  if (g.comErro > 0) {
    return { label: 'Com pendências', bg: '#fdf2e3', fg: '#b45309' };
  }
  if (g.emProcessamento > 0) {
    return { label: 'Em andamento', bg: '#e6f0fd', fg: '#1d4ed8' };
  }
  return { label: 'Finalizado', bg: '#e7f6ec', fg: '#15803d' };
}

export function GroupStatusChip({ group }: { group: DocumentGroup }) {
  const s = groupStatus(group);
  return <Chip size="small" label={s.label} sx={{ bgcolor: s.bg, color: s.fg, borderRadius: '20px', height: 24 }} />;
}
