import { useState, type ReactNode } from 'react';
import ReplayIcon from '@mui/icons-material/Replay';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import WarningAmberOutlinedIcon from '@mui/icons-material/WarningAmberOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import DataObjectIcon from '@mui/icons-material/DataObject';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useAuth } from '../auth/AuthContext';
import { useTrace } from './useTrace';
import { isFailure } from './StatusChip';
import { omissionsFromReason, omissionsOf, rejectionFields, type FieldError } from './platformReason';
import type { DocumentSummary, IntegrationStatus } from '../../types';

type Tab = 'source' | 'domain' | 'destination' | 'response';
const pretty = (v: unknown) => (typeof v === 'string' ? v : JSON.stringify(v, null, 2));

// Quem vê o JSON cru (as quatro fotos) atrás do "Visualizar JSON". Hoje só o Admin; um papel de Suporte, quando existir,
// entra aqui e no UserRole. É APRESENTAÇÃO, E NÃO AUTORIZAÇÃO: o /trace e o zip seguem acessíveis a qualquer usuário
// do tenant, Viewer incluído (ADR-0028), porque a lista do motivo e o chamado dependem deles. Restringir o JSON de fato é
// outra fatia (design D11 da change establishment-and-readable-dashboard).
const RAW_JSON_ROLES: readonly string[] = ['Admin'];
const canViewRawJson = (role?: string) => role !== undefined && RAW_JSON_ROLES.includes(role);

// Aceita pela plataforma: aqui o reason só pode ser a ressalva do envio (as omissões), nunca uma falha.
const ACCEPTED: IntegrationStatus[] = ['Submitted', 'Confirmed'];

export function DocumentDetail({ doc }: { doc: DocumentSummary }) {
  const { user } = useAuth();
  const { data, isLoading, isError } = useTrace(doc.tenantId, doc.naturalKey);
  const [tab, setTab] = useState<Tab>('source');
  const [showJson, setShowJson] = useState(false);
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
  const fields = failed ? rejectionFields(data) : null;

  return (
    <div style={{ padding: '18px 22px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Cabeçalho: a chave da nota */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16 }}>
        <div style={{ minWidth: 0, fontSize: 14, fontWeight: 600, color: 'var(--ink)', wordBreak: 'break-all', lineHeight: 1.4 }}>
          {doc.naturalKey}
        </div>
        {failed && (
          <button
            type="button"
            className="fh-btn"
            onClick={() => reprocess.mutate()}
            disabled={reprocess.isPending || reprocess.isSuccess}
            style={{ height: 32, flexShrink: 0 }}
          >
            <ReplayIcon sx={{ fontSize: 16 }} />
            {reprocess.isPending ? 'Reprocessando…' : reprocess.isSuccess ? 'Reenviado' : 'Reprocessar'}
          </button>
        )}
      </div>

      {/* Primeira vista: o motivo que se lê. Falha é erro, com a lista de campos da foto quando há; aceita com ressalva é
          a marca discreta; ignorada é aviso (ADR-0026). */}
      {doc.reason && !reprocess.isSuccess && (failed ? (
        <Banner tone="error" icon={<ErrorOutlineIcon sx={{ fontSize: 16, color: 'var(--error-text)' }} />}>
          {fields ? <FieldList fields={fields} /> : doc.reason}
        </Banner>
      ) : accepted ? (
        <RemarksMark omissions={omissionsOf(data) ?? omissionsFromReason(doc.reason)} />
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
          Não foi possível reprocessar: {(reprocess.error as Error)?.message}.
        </Banner>
      )}

      {/* O JSON cru fica atrás do botão, só para quem pode vê-lo. */}
      {canViewRawJson(user?.role) && (
        <div>
          <button
            type="button"
            className="fh-btn fh-btn-secondary"
            onClick={() => setShowJson((v) => !v)}
            aria-expanded={showJson}
            style={{ height: 30 }}
          >
            <DataObjectIcon sx={{ fontSize: 16 }} />
            {showJson ? 'Ocultar JSON' : 'Visualizar JSON'}
          </button>
        </div>
      )}

      {showJson && canViewRawJson(user?.role) && (
        <>
          {isLoading && <div style={{ padding: '20px 0', color: 'var(--muted)', fontSize: 13 }}>Carregando arquivos…</div>}

          {(isError || (!isLoading && !data)) && (
            <div style={{ padding: '20px 0', color: 'var(--muted)', fontSize: 13 }}>Sem arquivos para este documento ainda.</div>
          )}

          {data && (
            <div>
              {/* Abas */}
              <div style={{ display: 'flex', gap: 22, borderBottom: '1px solid var(--border)', marginBottom: 12 }}>
                <TabButton active={tab === 'source'} onClick={() => setTab('source')}>Origem</TabButton>
                <TabButton active={tab === 'domain'} onClick={() => setTab('domain')}>Domínio</TabButton>
                <TabButton active={tab === 'destination'} onClick={() => setTab('destination')}>Destino</TabButton>
                <TabButton active={tab === 'response'} onClick={() => setTab('response')}>Resposta</TabButton>
              </div>

              {tab === 'source' && (data.source ? <Code>{data.source}</Code> : <Empty />)}
              {tab === 'domain' && (data.domain !== undefined ? <Code>{pretty(data.domain)}</Code> : <Empty />)}
              {tab === 'destination' && (data.destination ? <Code>{pretty(data.destination.payload)}</Code> : <Empty />)}
              {tab === 'response' &&
                (data.responses ? (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                    <ResponseBlock title="Envio" envelope={data.responses.submit} />
                    <ResponseBlock title="Consulta de status" envelope={data.responses.status} />
                  </div>
                ) : (
                  <Empty />
                ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

// A recusa campo a campo: o caminho legível e, abaixo, as mensagens da plataforma, como vieram.
function FieldList({ fields }: { fields: FieldError[] }) {
  return (
    <div>
      <div style={{ fontWeight: 600, marginBottom: 6 }}>
        A plataforma de compliance recusou {fields.length === 1 ? '1 campo' : `${fields.length} campos`}:
      </div>
      <ul style={{ margin: 0, paddingLeft: 18, display: 'flex', flexDirection: 'column', gap: 5 }}>
        {fields.map((f) => (
          <li key={f.path}>
            <span style={{ fontWeight: 600 }}>{f.label}</span>
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

// Um envelope de resposta da plataforma (já redigido no servidor), ou a falta dele.
function ResponseBlock({ title, envelope }: { title: string; envelope: unknown }) {
  return (
    <div>
      <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--muted)', marginBottom: 6 }}>{title}</div>
      {envelope !== undefined ? <Code>{pretty(envelope)}</Code> : <Empty />}
    </div>
  );
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <div
      onClick={onClick}
      onMouseEnter={(e) => { if (!active) e.currentTarget.style.color = 'var(--text)'; }}
      onMouseLeave={(e) => { if (!active) e.currentTarget.style.color = 'var(--muted)'; }}
      style={{
        fontSize: 13.5,
        fontWeight: active ? 600 : 500,
        color: active ? 'var(--ink)' : 'var(--muted)',
        paddingBottom: 10,
        borderBottom: active ? '2px solid var(--accent)' : '2px solid transparent',
        marginBottom: -1,
        cursor: 'pointer',
      }}
    >
      {children}
    </div>
  );
}

function Code({ children }: { children: string }) {
  return (
    <pre
      className="fh-mono"
      style={{
        margin: 0,
        padding: '14px 16px',
        background: 'var(--surface-sunken)',
        color: 'var(--text)',
        border: '1px solid var(--border)',
        borderRadius: 8,
        fontSize: 12.5,
        lineHeight: 1.55,
        overflow: 'auto',
        maxHeight: 380,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
      }}
    >
      {children}
    </pre>
  );
}

function Empty() {
  return <div style={{ padding: '16px 0', color: 'var(--muted)', fontSize: 13 }}>Sem este arquivo.</div>;
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
