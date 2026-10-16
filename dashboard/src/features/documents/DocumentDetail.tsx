import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import ReplayIcon from '@mui/icons-material/Replay';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import WarningAmberOutlinedIcon from '@mui/icons-material/WarningAmberOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import DataObjectIcon from '@mui/icons-material/DataObject';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { canViewRawJson } from '../rawJson';
import { useInfo } from '../useInfo';
import { useReading } from './useReading';
import { isFailure, isReprocessable } from './StatusChip';
import { humanizePath, omissionsFromReason } from './platformReason';
import { RawJsonModal } from './RawJsonModal';
import type { DocumentSummary, FieldRejection, IntegrationStatus } from '../../types';

// Aceita pela plataforma: aqui o reason só pode ser a ressalva do envio (as omissões), nunca uma falha.
const ACCEPTED: IntegrationStatus[] = ['Submitted', 'Confirmed'];

// O motivo de o botão estar desativado. Mora **no próprio botão**, e não numa faixa na página: a explicação só faz
// sentido onde o botão existe. Numa nota que nunca teve reprocesso (Finalizado, Ignorado) não há botão, e aí não há
// nada a explicar — uma faixa ali atribuiria à chave uma indisponibilidade que não é dela.
const DISPATCH_OFF_HINT =
  'O envio ao conector de saída está desligado para este tenant. Ligue-o em Configurações → Conectores → Saída para voltar a enviar.';
const HINT_ID = 'fh-reprocess-off';

export function DocumentDetail({ doc }: { doc: DocumentSummary }) {
  // Quem vê as fotos cruas é o /info que diz; a tela não decide pelo papel (D7).
  const { data: info } = useInfo();
  // A primeira vista vem da leitura do desfecho, para qualquer papel; as fotos cruas só no modal do JSON.
  const { data: reading } = useReading(doc.tenantId, doc.naturalKey);
  const [jsonOpen, setJsonOpen] = useState(false);
  // O motivo do botão desativado aparece no hover, no foco por teclado e no clique/toque — as três, porque nem todo
  // usuário tem mouse.
  const [hintOpen, setHintOpen] = useState(false);
  // O retângulo deste botão é o que ancora a dica, que vive num portal fora daqui (ver `DispatchOffHint`).
  const reprocessRef = useRef<HTMLButtonElement>(null);
  const closeHint = useCallback(() => setHintOpen(false), []);
  const qc = useQueryClient();

  // Reprocessar: entrega o id ao adapter de entrada, que rebusca na origem e reintegra.
  const reprocess = useMutation({
    mutationFn: () => api.reprocess(doc.tenantId, doc.naturalKey),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['groups'] });
      qc.invalidateQueries({ queryKey: ['groupDocuments'] });
      qc.invalidateQueries({ queryKey: ['documents'] });
    },
  });

  const failed = isFailure(doc.status);
  const accepted = ACCEPTED.includes(doc.status);
  // Quem **teria** botão de reprocessar: as falhas e a nota em "Salva" (ADR-0041) — e é um predicado próprio, e não o
  // `isFailure`, porque "Salva" é reprocessável sem ser falha (ver `StatusChip.tsx`).
  const reprocessable = isReprocessable(doc.status);
  // A chave de envio do tenant (ADR-0041) decide se esse botão sai habilitado ou desativado-com-motivo. Desligada, o
  // reprocesso não acontece em nota nenhuma — a chave é porta única da saída, e o clique manual não passa por cima
  // dela. Três estados, e o terceiro importa: enquanto o /info não respondeu, ou se ele falhou, a tela não afirma
  // nenhum dos dois — o botão fica AUSENTE, nem habilitado nem desativado. É a mesma regra do canViewRawJson.
  const showReprocess = reprocessable && info?.outboundDispatch !== undefined;
  const reprocessOff = showReprocess && info?.outboundDispatch === false;
  const fields = failed && reading && reading.fields.length > 0 ? reading.fields : null;
  const omissions = reading && reading.omissions.length > 0 ? reading.omissions : null;

  return (
    <div style={{ padding: '18px 22px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Cabeçalho: a chave da nota */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16 }}>
        <div style={{ minWidth: 0, fontSize: 14, fontWeight: 600, color: 'var(--ink)', wordBreak: 'break-all', lineHeight: 1.4 }}>
          {doc.naturalKey}
        </div>
        <div style={{ display: 'flex', gap: 8, flexShrink: 0 }}>
          {/* O JSON cru abre num modal próprio, só para quem o /info disser (o /trace dá 403 para os demais). Antes de
              ele responder, ou se falhar, o botão não aparece — para papel nenhum. */}
          {canViewRawJson(info) && (
            <button type="button" className="fh-btn fh-btn-secondary" onClick={() => setJsonOpen(true)} style={{ height: 32 }}>
              <DataObjectIcon sx={{ fontSize: 16 }} />
              Visualizar JSON
            </button>
          )}
          {/* Com o envio desligado o botão fica VISÍVEL e desativado, com o motivo nele: um botão que some deixa o
              usuário procurando o que não existe, e um desativado mudo o deixa olhando um botão morto. Desativado é
              `aria-disabled`, e não o atributo `disabled` — o `disabled` não recebe hover nem foco em vários
              navegadores, e aí o motivo nunca chegaria a quem precisa. Quem segura o clique é a guarda no onClick. */}
          {showReprocess && (
            <div
              style={{ display: 'flex' }}
              onMouseEnter={() => reprocessOff && setHintOpen(true)}
              onMouseLeave={closeHint}
            >
              <button
                type="button"
                ref={reprocessRef}
                className="fh-btn"
                onClick={() => (reprocessOff ? setHintOpen((v) => !v) : reprocess.mutate())}
                onFocus={() => reprocessOff && setHintOpen(true)}
                onBlur={closeHint}
                aria-disabled={reprocessOff || undefined}
                aria-describedby={reprocessOff ? HINT_ID : undefined}
                disabled={!reprocessOff && (reprocess.isPending || reprocess.isSuccess)}
                style={reprocessOff ? { height: 32, opacity: 0.55, cursor: 'not-allowed' } : { height: 32 }}
              >
                <ReplayIcon sx={{ fontSize: 16 }} />
                {reprocess.isPending ? 'Reprocessando…' : reprocess.isSuccess ? 'Reenviado' : 'Reprocessar'}
              </button>
              {reprocessOff && hintOpen && <DispatchOffHint anchorRef={reprocessRef} onDismiss={closeHint} />}
            </div>
          )}
        </div>
      </div>

      {/* Primeira vista: o motivo que se lê. Falha é erro, com a lista de campos da leitura quando há; aceita com
          ressalva é a marca discreta; ignorada e salva são aviso (ADR-0026). */}
      {doc.reason && !reprocess.isSuccess && (failed ? (
        <Banner tone="error" icon={<ErrorOutlineIcon sx={{ fontSize: 16, color: 'var(--error-text)' }} />}>
          {fields ? <FieldList fields={fields} /> : doc.reason}
        </Banner>
      ) : accepted ? (
        <RemarksMark omissions={omissions ?? omissionsFromReason(doc.reason)} />
      ) : (
        <Banner tone="warn" icon={<WarningAmberOutlinedIcon sx={{ fontSize: 16, color: 'var(--warn-text)' }} />}>{doc.reason}</Banner>
      ))}
      {reprocess.isSuccess && (
        <Banner tone="ok" icon={<CheckCircleOutlineIcon sx={{ fontSize: 16, color: 'var(--ok-text)' }} />}>
          Nota reenviada à origem para reprocessar. O status atualiza em instantes.
        </Banner>
      )}
      {reprocess.isError && (
        <Banner tone="error" icon={<ErrorOutlineIcon sx={{ fontSize: 16, color: 'var(--error-text)' }} />}>
          {/* O motivo vem do servidor (a nota fora da origem, ou a leitura da origem em falha), já com o ponto final. */}
          Não foi possível reprocessar: {(reprocess.error as Error)?.message.replace(/\.$/, '')}.
        </Banner>
      )}

      {jsonOpen && <RawJsonModal tenantId={doc.tenantId} naturalKey={doc.naturalKey} onClose={() => setJsonOpen(false)} />}
    </div>
  );
}

const HINT_WIDTH = 272;
const HINT_GAP = 6;      // a folga entre o botão e a dica
const HINT_EDGE = 8;     // a folga mínima da borda da janela
// Acima do modal, que é 50 — a dica é do botão que está dentro dele.
const HINT_Z = 60;

// Onde a dica fica, a partir do retângulo do botão. Pura, para o cálculo não depender de quando o React renderiza.
function hintPlacement(anchor: HTMLElement | null, height: number) {
  if (!anchor) {
    return { top: HINT_EDGE, right: HINT_EDGE };
  }
  const r = anchor.getBoundingClientRect();
  // Alinhada à direita do botão, e presa à janela dos dois lados: a dica não sai da tela em lado nenhum.
  const right = Math.max(HINT_EDGE, Math.min(window.innerWidth - r.right, window.innerWidth - HINT_WIDTH - HINT_EDGE));
  const below = r.bottom + HINT_GAP;
  const cabeAbaixo = below + height <= window.innerHeight - HINT_EDGE;
  const cabeAcima = r.top - HINT_GAP - height >= HINT_EDGE;
  return cabeAbaixo || !cabeAcima
    ? { top: Math.max(HINT_EDGE, below), right }
    : { bottom: window.innerHeight - r.top + HINT_GAP, right };
}

/** A explicação do botão desativado, num **portal** para o `document.body`.
 *
 *  Ela não pode morar no fluxo do detalhe: o corpo do modal é `overflow: auto` (`components/Modal.tsx`), e um balão
 *  `position: absolute` ali dentro cresce o `scrollHeight` do corpo e faz aparecer **barra de rolagem** no modal —
 *  um tooltip não mexe no layout de quem o hospeda. No portal ela é `position: fixed`, ancorada no retângulo do
 *  próprio botão.
 *
 *  O portal muda o lugar no **DOM**, e não na **árvore React**: o clique nela continua subindo até o cartão do modal,
 *  onde o `stopPropagation` o segura — então clicar na dica não fecha o modal (tem teste).
 *
 *  Enquanto está aberta, a dica fecha na rolagem (em **captura**, que é como se escuta a rolagem do corpo do modal) e
 *  no redimensionamento: uma dica ancorada num botão que se moveu é pior do que uma dica que fechou. */
function DispatchOffHint({ anchorRef, onDismiss }: { anchorRef: RefObject<HTMLButtonElement>; onDismiss: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [place, setPlace] = useState(() => hintPlacement(anchorRef.current, 0));

  // Antes da pintura: a dica já está no DOM, então dá para medir a altura dela e só então decidir abaixo × acima.
  useLayoutEffect(() => {
    setPlace(hintPlacement(anchorRef.current, ref.current?.offsetHeight ?? 0));
  }, [anchorRef]);

  useEffect(() => {
    window.addEventListener('scroll', onDismiss, true);
    window.addEventListener('resize', onDismiss);
    return () => {
      window.removeEventListener('scroll', onDismiss, true);
      window.removeEventListener('resize', onDismiss);
    };
  }, [onDismiss]);

  return createPortal(
    <div
      id={HINT_ID}
      role="tooltip"
      ref={ref}
      style={{
        position: 'fixed', ...place, zIndex: HINT_Z, width: HINT_WIDTH,
        border: '1px solid var(--warn-border)', background: 'var(--warn-bg)', color: 'var(--warn-text)',
        borderRadius: 8, padding: '8px 10px', fontSize: 12, lineHeight: 1.45, textAlign: 'left',
        boxShadow: 'var(--shadow-card)',
      }}
    >
      {DISPATCH_OFF_HINT}
    </div>,
    document.body,
  );
}

// A recusa campo a campo: o caminho legível e, abaixo, as mensagens da plataforma, como vieram.
function FieldList({ fields }: { fields: FieldRejection[] }) {
  return (
    <div>
      <div style={{ fontWeight: 600, marginBottom: 6 }}>
        A plataforma de compliance recusou {fields.length === 1 ? '1 campo' : `${fields.length} campos`}:
      </div>
      <ul style={{ margin: 0, paddingLeft: 18, display: 'flex', flexDirection: 'column', gap: 5 }}>
        {fields.map((f) => (
          <li key={f.path}>
            <span style={{ fontWeight: 600 }}>{humanizePath(f.path)}</span>
            {f.messages.map((m, i) => (
              <div key={i}>{m}</div>
            ))}
          </li>
        ))}
      </ul>
    </div>
  );
}

// A marca discreta da nota aceita com omissões: o único sinal de que o hub descartou um campo do documento do cliente.
function RemarksMark({ omissions }: { omissions: string[] }) {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        style={{
          display: 'inline-flex', alignItems: 'center', gap: 6, border: '1px solid var(--warn-border)', background: 'var(--warn-bg)',
          color: 'var(--warn-text)', borderRadius: 999, padding: '3px 10px', fontSize: 12, fontWeight: 600, cursor: 'pointer',
        }}
      >
        <WarningAmberOutlinedIcon sx={{ fontSize: 14 }} />
        Enviado com ressalvas
      </button>
      {open && (
        <div style={{ marginTop: 8, fontSize: 12.5, color: 'var(--text)' }}>
          <div style={{ color: 'var(--muted)', marginBottom: 4 }}>O que o documento tem e o contrato do destino não levou:</div>
          <ul style={{ margin: 0, paddingLeft: 18 }}>
            {omissions.map((o) => (
              <li key={o}>{o}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

const BANNER_TONES = {
  ok: { bg: 'var(--ok-bg)', border: 'var(--ok-border)', fg: 'var(--ok-text)' },
  warn: { bg: 'var(--warn-bg)', border: 'var(--warn-border)', fg: 'var(--warn-text)' },
  error: { bg: 'var(--error-bg)', border: 'var(--error-border)', fg: 'var(--error-text)' },
};

function Banner({ tone, icon, children }: { tone: keyof typeof BANNER_TONES; icon: ReactNode; children: ReactNode }) {
  const t = BANNER_TONES[tone];
  return (
    <div style={{ border: `1px solid ${t.border}`, background: t.bg, borderRadius: 8, padding: '10px 12px', display: 'flex', gap: 9, alignItems: 'flex-start' }}>
      <span style={{ flexShrink: 0, marginTop: 1, display: 'grid', placeItems: 'center' }}>{icon}</span>
      <div style={{ fontSize: 12.5, lineHeight: 1.45, color: t.fg }}>{children}</div>
    </div>
  );
}
