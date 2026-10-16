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
  // Fora do escopo da esteira (NFS-e, CT-e, nota não autorizada) — desfecho explícito, não é falha (ADR-0025).
  Ignored: { label: 'Ignorado', tone: 'pending' },
  // Salva sem envio, porque a chave de envio do tenant está desligada (ADR-0040). Reusa o tom `pending`, o mesmo do
  // "Pendente" e do "Ignorado": é parada deliberada, e não alarme — neutro é a cor certa, e o rótulo faz a distinção.
  Saved: { label: 'Salva', tone: 'pending' },
};

// Status de falha — a nota que deu errado. É esta lista que diz "isto é erro": ela espelha a faixa `Failed` do
// `SqlDocumentQueries`, que alimenta o filtro de falhas e a contagem do card "Com erro", e é por ela que o detalhe
// mostra a faixa vermelha com a recusa campo a campo.
export const FAILURE_STATUSES: IntegrationStatus[] = ['IntegrationError', 'Unconfirmed', 'DeadLettered'];
export const isFailure = (s: IntegrationStatus) => FAILURE_STATUSES.includes(s);

// Status que o reprocesso manual tira do lugar — as falhas **mais** a "Salva" (ADR-0040).
//
// **Por que são duas listas, e não uma.** "Salva" é reprocessável mas NÃO é falha: a nota foi gravada de propósito,
// com o envio desligado, e o reprocesso manual é — e só ele é — o caminho de saída desse estado (ADR-0040). Unificar
// as duas quebra de um lado ou do outro, e as duas quebras são silenciosas:
// - pôr `'Saved'` em `FAILURE_STATUSES` faz a nota salva entrar no filtro de falhas e contar como erro, contra a spec
//   (`"Salva" é situação própria na tela` — não é falha, como a ignorada);
// - usar `isFailure` para decidir o botão tira o reprocesso da nota em "Salva", e ela fica sem saída nenhuma pela
//   tela — foi exatamente o defeito que a conferência de 2026-10-16 achou.
export const REPROCESSABLE_STATUSES: IntegrationStatus[] = [...FAILURE_STATUSES, 'Saved'];
export const isReprocessable = (s: IntegrationStatus) => REPROCESSABLE_STATUSES.includes(s);

export function StatusChip({ status }: { status: IntegrationStatus }) {
  const s = map[status] ?? { label: status, tone: 'pending' as Tone };
  return <TokenChip tone={s.tone}>{s.label}</TokenChip>;
}
