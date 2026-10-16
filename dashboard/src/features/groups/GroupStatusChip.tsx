import { StatusChip, type Tone } from '../../components/StatusChip';
import type { DocumentGroup } from '../../types';

// Status agregado do grupo, derivado das contagens → tom do design v3.
export function groupStatus(g: DocumentGroup): { label: string; tone: Tone } {
  // O erro tem precedência: um grupo com 7 salvas e 1 rejeitada diz "Com pendências", porque é a rejeitada que precisa
  // de alguém.
  if (g.comErro > 0) {
    return { label: 'Com pendências', tone: 'partial' };
  }
  if (g.emProcessamento > 0) {
    return { label: 'Processando', tone: 'info' };
  }
  // Antes deste estado, um grupo só de notas salvas caía em "Finalizado" com as três faixas zeradas — mentira de tela
  // (ADR-0041). O tom é o `pending`, o mesmo do selo da nota: parada deliberada, e não alarme.
  if (g.salvas > 0) {
    return { label: 'Salvas', tone: 'pending' };
  }
  return { label: 'Finalizado', tone: 'ok' };
}

export function GroupStatusChip({ group }: { group: DocumentGroup }) {
  const s = groupStatus(group);
  return <StatusChip tone={s.tone}>{s.label}</StatusChip>;
}
