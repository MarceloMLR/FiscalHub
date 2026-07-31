import { StatusChip, type Tone } from '../../components/StatusChip';
import type { DocumentGroup } from '../../types';

// Status agregado do grupo, derivado das contagens → tom do design v3.
export function groupStatus(g: DocumentGroup): { label: string; tone: Tone } {
  if (g.comErro > 0) {
    return { label: 'Com pendências', tone: 'partial' };
  }
  if (g.emProcessamento > 0) {
    return { label: 'Processando', tone: 'info' };
  }
  return { label: 'Finalizado', tone: 'ok' };
}

export function GroupStatusChip({ group }: { group: DocumentGroup }) {
  const s = groupStatus(group);
  return <StatusChip tone={s.tone}>{s.label}</StatusChip>;
}
