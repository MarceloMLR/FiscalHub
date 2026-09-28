import { useState, type ReactNode } from 'react';
import ReplayIcon from '@mui/icons-material/Replay';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import WarningAmberOutlinedIcon from '@mui/icons-material/WarningAmberOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import DataObjectIcon from '@mui/icons-material/DataObject';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useAuth } from '../auth/AuthContext';
import { canViewRawJson } from '../auth/roles';
import { useReading } from './useReading';
import { isFailure } from './StatusChip';
import { humanizePath, omissionsFromReason } from './platformReason';
import { RawJsonModal } from './RawJsonModal';
import type { DocumentSummary, FieldRejection, IntegrationStatus } from '../../types';

// Aceita pela plataforma: aqui o reason só pode ser a ressalva do envio (as omissões), nunca uma falha.
const ACCEPTED: IntegrationStatus[] = ['Submitted', 'Confirmed'];

export function DocumentDetail({ doc }: { doc: DocumentSummary }) {
  const { user } = useAuth();
  // A primeira vista vem da leitura do desfecho, para qualquer papel; as fotos cruas só no modal do JSON.
  const { data: reading } = useReading(doc.tenantId, doc.naturalKey);
  const [jsonOpen, setJsonOpen] = useState(false);
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
          {/* O JSON cru abre num modal próprio, só para quem tem o papel (o /trace dá 403 para os demais). */}
          {canViewRawJson(user?.role) && (
            <button type="button" className="fh-btn fh-btn-secondary" onClick={() => setJsonOpen(true)} style={{ height: 32 }}>
              <DataObjectIcon sx={{ fontSize: 16 }} />
              Visualizar JSON
            </button>
          )}
          {failed && (
            <button
              type="button"
              className="fh-btn"
              onClick={() => reprocess.mutate()}
              disabled={reprocess.isPending || reprocess.isSuccess}
              style={{ height: 32 }}
            >
              <ReplayIcon sx={{ fontSize: 16 }} />
              {reprocess.isPending ? 'Reprocessando…' : reprocess.isSuccess ? 'Reenviado' : 'Reprocessar'}
            </button>
          )}
        </div>
      </div>

      {/* Primeira vista: o motivo que se lê. Falha é erro, com a lista de campos da leitura quando há; aceita com
          ressalva é a marca discreta; ignorada é aviso (ADR-0026). */}
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
          Não foi possível reprocessar: {(reprocess.error as Error)?.message}.
        </Banner>
      )}

      {jsonOpen && <RawJsonModal tenantId={doc.tenantId} naturalKey={doc.naturalKey} onClose={() => setJsonOpen(false)} />}
    </div>
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
