import { useState, type CSSProperties, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import ConfirmationNumberOutlinedIcon from '@mui/icons-material/ConfirmationNumberOutlined';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import AttachFileOutlinedIcon from '@mui/icons-material/AttachFileOutlined';
import CloseIcon from '@mui/icons-material/Close';
import { Modal } from '../../components/Modal';
import { StatusChip } from '../documents/StatusChip';
import { api } from '../../api/client';
import type { DocumentSummary } from '../../types';

/**
 * Abre um chamado de suporte para uma ou mais notas. O usuário informa título e descrição; os logs
 * (origem/domínio/destino) de cada nota vão anexados, zipados por nota, junto com as infos de
 * integração. Interativo: mostra as notas selecionadas e o resultado com link do chamado.
 */
export function TicketModal({ notes, onClose }: { notes: DocumentSummary[]; onClose: () => void }) {
  const many = notes.length > 1;
  const [subject, setSubject] = useState(
    many ? `Chamado — ${notes.length} notas com problema` : `Chamado — nota ${notes[0]?.number ?? notes[0]?.naturalKey ?? ''}`,
  );
  const [description, setDescription] = useState('');
  const [files, setFiles] = useState<File[]>([]);

  const addFiles = (list: FileList | null) => {
    if (!list || list.length === 0) return;
    // Captura os arquivos AGORA: o onChange limpa o input logo em seguida (value=''),
    // o que esvazia o FileList antes do updater do setState rodar. Sem isto, nada é adicionado.
    const picked = Array.from(list);
    setFiles((prev) => [...prev, ...picked]);
  };
  const removeFile = (idx: number) => setFiles((prev) => prev.filter((_, i) => i !== idx));

  const keys = notes.map((n) => n.naturalKey);
  const estimate = useQuery({
    queryKey: ['ticketEstimate', keys],
    queryFn: () => api.estimateTicketLogs(keys),
    enabled: notes.length > 0,
  });

  const open = useMutation({
    mutationFn: () => api.openTicket(subject.trim(), description.trim(), keys, files),
  });

  const done = open.data;
  const fmtMB = (b: number) => (b / (1024 * 1024)).toFixed(1);
  const logsBytes = estimate.data?.logsBytes ?? 0;
  const limitBytes = estimate.data?.limitBytes ?? 20 * 1024 * 1024;
  const extraBytes = files.reduce((s, f) => s + f.size, 0);
  const usedBytes = logsBytes + extraBytes;
  const remainingBytes = Math.max(0, limitBytes - usedBytes);
  const overLimit = usedBytes > limitBytes;
  const valid = notes.length > 0 && subject.trim().length > 0 && description.trim().length > 0 && !overLimit;

  return (
    <Modal
      title="Abrir chamado"
      subtitle={many ? `${notes.length} notas selecionadas` : notes[0]?.number ?? notes[0]?.naturalKey}
      onClose={onClose}
      maxWidth={520}
      footer={
        done ? (
          <button type="button" className="fh-btn" onClick={onClose} style={{ height: 34 }}>
            Fechar
          </button>
        ) : (
          <>
            <button type="button" className="fh-btn fh-btn-secondary" onClick={onClose} style={{ height: 34 }}>
              Cancelar
            </button>
            <button type="button" className="fh-btn" onClick={() => open.mutate()} disabled={!valid || open.isPending} style={{ height: 34 }}>
              <ConfirmationNumberOutlinedIcon sx={{ fontSize: 16 }} />
              {open.isPending ? 'Abrindo…' : 'Abrir chamado'}
            </button>
          </>
        )
      }
    >
      <div style={{ padding: '18px 22px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        {done ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <Banner tone="ok">
              Chamado <strong>{done.ticketId}</strong> aberto com os logs das notas anexados.
            </Banner>
            {done.url && !done.url.startsWith('local://') && (
              <a href={done.url} target="_blank" rel="noreferrer" className="fh-btn" style={{ height: 34, width: 'fit-content', textDecoration: 'none' }}>
                <OpenInNewIcon sx={{ fontSize: 16 }} />
                Ver chamado
              </a>
            )}
            {done.url?.startsWith('local://') && (
              <div style={{ fontSize: 12, color: 'var(--muted)' }}>
                Ambiente de desenvolvimento (mock). Com o Freshdesk configurado, aqui viria o link real do chamado.
              </div>
            )}
          </div>
        ) : (
          <>
            <Field label="Título">
              <input style={inputStyle} value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="Resumo do problema" />
            </Field>
            <Field label="Descrição" hint="Descreva o problema. Os logs e as infos de integração vão anexados.">
              <textarea
                style={{ ...inputStyle, height: 96, padding: '9px 11px', resize: 'vertical', lineHeight: 1.5 }}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="O que aconteceu, desde quando, o que já tentaram…"
              />
            </Field>

            <div>
              <div className="fh-label" style={{ fontSize: 11, letterSpacing: '0.06em', marginBottom: 7 }}>
                Notas no chamado ({notes.length})
              </div>
              <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'hidden', maxHeight: 168, overflowY: 'auto' }}>
                {notes.map((n, i) => (
                  <div
                    key={n.naturalKey}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 10,
                      padding: '9px 12px',
                      borderTop: i === 0 ? 'none' : '1px solid var(--border)',
                    }}
                  >
                    <span style={{ fontSize: 13, color: 'var(--text)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      {n.number ?? n.naturalKey}
                    </span>
                    <StatusChip status={n.status} />
                  </div>
                ))}
              </div>
              <div style={{ fontSize: 11.5, color: 'var(--muted)', marginTop: 7 }}>
                Os logs de cada nota são anexados automaticamente na abertura do chamado.
              </div>
            </div>

            {/* Anexos extras opcionais do usuário */}
            <div>
              <div className="fh-label" style={{ fontSize: 11, letterSpacing: '0.06em', marginBottom: 7 }}>
                Anexos extras (opcional)
              </div>
              <label
                className="fh-btn fh-btn-secondary"
                style={{ height: 32, width: 'fit-content', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }}
              >
                <AttachFileOutlinedIcon sx={{ fontSize: 16 }} />
                Adicionar arquivo
                <input
                  type="file"
                  multiple
                  onChange={(e) => { addFiles(e.target.files); e.currentTarget.value = ''; }}
                  style={{ display: 'none' }}
                />
              </label>
              {files.length > 0 && (
                <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
                  {files.map((f, i) => (
                    <div
                      key={i}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        gap: 10,
                        border: '1px solid var(--border)',
                        borderRadius: 8,
                        padding: '7px 10px',
                      }}
                    >
                      <span style={{ fontSize: 12.5, color: 'var(--text)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {f.name}
                      </span>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0 }}>
                        <span style={{ fontSize: 11.5, color: 'var(--muted)' }}>{(f.size / 1024).toFixed(0)} KB</span>
                        <button
                          type="button"
                          onClick={() => removeFile(i)}
                          aria-label="Remover anexo"
                          style={{ display: 'grid', placeItems: 'center', width: 22, height: 22, borderRadius: 6, border: 'none', background: 'transparent', color: 'var(--muted)', cursor: 'pointer' }}
                        >
                          <CloseIcon sx={{ fontSize: 15 }} />
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
              <div style={{ fontSize: 11.5, marginTop: 8, color: overLimit ? 'var(--error-text)' : 'var(--muted)' }}>
                {estimate.isLoading ? (
                  'Calculando o tamanho dos logs…'
                ) : (
                  <>
                    Logs automáticos <strong>{fmtMB(logsBytes)} MB</strong> · seus anexos{' '}
                    <strong>{fmtMB(extraBytes)} MB</strong> · disponível{' '}
                    <strong>{fmtMB(remainingBytes)} MB</strong> de {fmtMB(limitBytes)} MB
                  </>
                )}
              </div>
            </div>

            {overLimit && <Banner tone="err">O total (logs + anexos) passa de {fmtMB(limitBytes)} MB. Remova arquivos para abrir o chamado.</Banner>}
            {open.isError && <Banner tone="err">{(open.error as Error).message}</Banner>}
          </>
        )}
      </div>
    </Modal>
  );
}

const inputStyle: CSSProperties = {
  width: '100%',
  boxSizing: 'border-box',
  height: 38,
  padding: '0 11px',
  borderRadius: 8,
  border: '1px solid var(--border)',
  background: 'var(--surface)',
  color: 'var(--text)',
  fontSize: 13.5,
  fontFamily: 'inherit',
  outline: 'none',
};

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <span className="fh-label" style={{ fontSize: 11, letterSpacing: '0.06em' }}>{label}</span>
      {children}
      {hint && <span style={{ fontSize: 11.5, color: 'var(--muted)' }}>{hint}</span>}
    </label>
  );
}

function Banner({ tone, children }: { tone: 'ok' | 'err'; children: ReactNode }) {
  const t = tone === 'ok'
    ? { bg: 'var(--ok-bg)', border: 'var(--ok-border)', fg: 'var(--ok-text)' }
    : { bg: 'var(--error-bg)', border: 'var(--error-border)', fg: 'var(--error-text)' };
  return (
    <div style={{ border: `1px solid ${t.border}`, background: t.bg, color: t.fg, borderRadius: 8, padding: '10px 12px', fontSize: 12.5, lineHeight: 1.45 }}>
      {children}
    </div>
  );
}
