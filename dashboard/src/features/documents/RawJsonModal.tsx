import { useState, type ReactNode } from 'react';
import { Modal } from '../../components/Modal';
import { useTrace } from './useTrace';

type Tab = 'source' | 'domain' | 'destination' | 'response';
const pretty = (v: unknown) => (typeof v === 'string' ? v : JSON.stringify(v, null, 2));

// O JSON cru de uma nota, num modal próprio por cima do detalhe, com uma aba por foto. Só é aberto por quem tem o papel
// (RAW_JSON_ROLES); o /trace dá 403 para os demais. As fotos só são pedidas com o modal aberto.
export function RawJsonModal({ tenantId, naturalKey, onClose }: { tenantId: string; naturalKey: string; onClose: () => void }) {
  const { data, isLoading, isError } = useTrace(tenantId, naturalKey);
  const [tab, setTab] = useState<Tab>('source');

  return (
    <Modal title="JSON da nota" subtitle={naturalKey} onClose={onClose} maxWidth={880}>
      <div style={{ padding: '14px 22px 22px' }}>
        {isLoading && <div style={{ padding: '20px 0', color: 'var(--muted)', fontSize: 13 }}>Carregando arquivos…</div>}

        {(isError || (!isLoading && !data)) && (
          <div style={{ padding: '20px 0', color: 'var(--muted)', fontSize: 13 }}>Sem arquivos para este documento ainda.</div>
        )}

        {data && (
          <div>
            {/* Qual JSON: uma aba por foto */}
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
      </div>
    </Modal>
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
        maxHeight: '60vh',
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
