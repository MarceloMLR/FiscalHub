import { StatusChip as TokenChip, type Tone } from '../../components/StatusChip';
import type { IntegrationStatus } from '../../types';

// Status na língua do usuário fiscal → rótulo + tom do design system v3 (claro/escuro juntos).
const map: Record<IntegrationStatus, { label: string; tone: Tone }> = {
  Pending: { label: 'Pendente', tone: 'pending' },
  Submitted: { label: 'Processando', tone: 'info' },
  Confirmed: { label: 'Finalizado', tone: 'ok' },
  IntegrationError: { label: 'Rejeitado', tone: 'error' },
  Unconfirmed: { label: 'Sem retorno', tone: 'warn' },
  DeadLettered: { label: 'Falha', tone: 'dead' },
};

// Status de falha — habilitam o reprocessamento (rebuscar na origem e reintegrar).
export const FAILURE_STATUSES: IntegrationStatus[] = ['IntegrationError', 'Unconfirmed', 'DeadLettered'];
export const isFailure = (s: IntegrationStatus) => FAILURE_STATUSES.includes(s);

export function StatusChip({ status }: { status: IntegrationStatus }) {
  const s = map[status] ?? { label: status, tone: 'pending' as Tone };
  return <TokenChip tone={s.tone}>{s.label}</TokenChip>;
}
