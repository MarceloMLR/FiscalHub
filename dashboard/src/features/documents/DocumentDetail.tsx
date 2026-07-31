import { useState, type ReactNode } from 'react';
import ReplayIcon from '@mui/icons-material/Replay';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useTrace } from './useTrace';
import { isFailure } from './StatusChip';
import type { DocumentSummary } from '../../types';

type Tab = 'source' | 'domain' | 'destination';
const pretty = (v: unknown) => (typeof v === 'string' ? v : JSON.stringify(v, null, 2));

export function DocumentDetail({ doc }: { doc: DocumentSummary }) {
  const { data, isLoading, isError } = useTrace(doc.tenantId, doc.naturalKey);
  const [tab, setTab] = useState<Tab>('source');
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

  return (
    <div style={{ padding: '18px 22px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Cabeçalho */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--ink)', wordBreak: 'break-all', lineHeight: 1.4 }}>
            {doc.naturalKey}
          </div>
          <div style={{ fontSize: 12.5, color: 'var(--muted)', marginTop: 2 }}>Rastreabilidade: origem → domínio → destino</div>
        </div>
        {isFailure(doc.status) && (
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

      {doc.reason && !reprocess.isSuccess && (
        <Banner tone="error" icon={<ErrorOutlineIcon sx={{ fontSize: 16, color: 'var(--error-text)' }} />}>{doc.reason}</Banner>
      )}
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
          </div>

          {tab === 'source' && (data.source ? <Code>{data.source}</Code> : <Empty />)}
          {tab === 'domain' && (data.domain !== undefined ? <Code>{pretty(data.domain)}</Code> : <Empty />)}
          {tab === 'destination' && (data.destination ? <Code>{pretty(data.destination.payload)}</Code> : <Empty />)}
        </div>
      )}
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

function Banner({ tone, icon, children }: { tone: 'ok' | 'error'; icon: ReactNode; children: ReactNode }) {
  const t = tone === 'ok'
    ? { bg: 'var(--ok-bg)', border: 'var(--ok-border)', fg: 'var(--ok-text)' }
    : { bg: 'var(--error-bg)', border: 'var(--error-border)', fg: 'var(--error-text)' };
  return (
    <div style={{ border: `1px solid ${t.border}`, background: t.bg, borderRadius: 8, padding: '10px 12px', display: 'flex', gap: 9, alignItems: 'flex-start' }}>
      <span style={{ flexShrink: 0, marginTop: 1, display: 'grid', placeItems: 'center' }}>{icon}</span>
      <div style={{ fontSize: 12.5, lineHeight: 1.45, color: t.fg }}>{children}</div>
    </div>
  );
}
