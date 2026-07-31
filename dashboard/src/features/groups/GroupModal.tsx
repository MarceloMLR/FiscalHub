import { useState } from 'react';
import ConfirmationNumberOutlinedIcon from '@mui/icons-material/ConfirmationNumberOutlined';
import { Modal } from '../../components/Modal';
import { useGroupDocuments } from './useGroups';
import { StatusChip } from '../documents/StatusChip';
import { NoteDialog } from './NoteDialog';
import { TicketModal } from '../support/TicketModal';
import type { DocumentGroup, DocumentSummary } from '../../types';

const GRID = '34px minmax(120px,1.6fr) 80px minmax(130px,1.2fr) 90px minmax(150px,1.3fr)';

function dateTime(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

export function GroupModal({ group, onClose }: { group: DocumentGroup | null; onClose: () => void }) {
  const { data: docs } = useGroupDocuments(group?.companyCode, group?.branchCode, group?.referenceDate);
  const [note, setNote] = useState<DocumentSummary | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [ticketOpen, setTicketOpen] = useState(false);

  if (!group) {
    return null;
  }
  const rows = docs ?? [];
  const toggle = (key: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });
  const selectedNotes = rows.filter((d) => selected.has(d.naturalKey));

  return (
    <>
      <Modal
        title={<>Empresa {group.companyCode} · Filial {group.branchCode}</>}
        subtitle={`${group.referenceDate} · ${group.total} ${group.total === 1 ? 'nota' : 'notas'}`}
        onClose={onClose}
        maxWidth={780}
        footer={
          <>
            <span style={{ fontSize: 12.5, color: 'var(--muted)', marginRight: 'auto' }}>
              {selected.size > 0
                ? `${selected.size} selecionada${selected.size > 1 ? 's' : ''}`
                : 'Selecione ao menos uma nota'}
            </span>
            <button
              type="button"
              className="fh-btn"
              onClick={() => setTicketOpen(true)}
              disabled={selected.size === 0}
              style={{ height: 34 }}
            >
              <ConfirmationNumberOutlinedIcon sx={{ fontSize: 16 }} />
              Abrir chamado{selected.size > 0 ? ` (${selected.size})` : ''}
            </button>
          </>
        }
      >
        {/* Cabeçalho da tabela */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: GRID,
            columnGap: 12,
            alignItems: 'center',
            padding: '11px 22px',
            background: 'var(--surface-2)',
            borderBottom: '1px solid var(--border)',
            fontSize: 10.5,
            fontWeight: 700,
            letterSpacing: '0.075em',
            textTransform: 'uppercase',
            color: 'var(--muted)',
          }}
        >
          <div />
          <div>Número</div>
          <div>Modelo</div>
          <div>Status</div>
          <div style={{ textAlign: 'right' }}>Consultas</div>
          <div style={{ textAlign: 'right' }}>Atualizado</div>
        </div>

        {rows.map((d, i) => (
          <div
            key={d.naturalKey}
            onClick={() => setNote(d)}
            style={{
              display: 'grid',
              gridTemplateColumns: GRID,
              columnGap: 12,
              alignItems: 'center',
              padding: '12px 22px',
              borderBottom: i === rows.length - 1 ? 'none' : '1px solid var(--border)',
              fontSize: 13.5,
              cursor: 'pointer',
              background: selected.has(d.naturalKey) ? 'var(--accent-tint)' : 'transparent',
            }}
            onMouseEnter={(e) => { if (!selected.has(d.naturalKey)) e.currentTarget.style.background = 'var(--surface-2)'; }}
            onMouseLeave={(e) => { e.currentTarget.style.background = selected.has(d.naturalKey) ? 'var(--accent-tint)' : 'transparent'; }}
          >
            <input
              type="checkbox"
              checked={selected.has(d.naturalKey)}
              onClick={(e) => e.stopPropagation()}
              onChange={() => toggle(d.naturalKey)}
              style={{ width: 15, height: 15, cursor: 'pointer', accentColor: 'var(--accent)' }}
            />
            <span style={{ color: 'var(--text)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              {d.number ?? d.naturalKey}
            </span>
            <span style={{ color: 'var(--text)' }}>{d.model ?? '—'}</span>
            <span><StatusChip status={d.status} /></span>
            <span style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: 'var(--text)' }}>{d.attempts}</span>
            <span style={{ textAlign: 'right', fontSize: 12.5, color: 'var(--text-secondary)', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
              {dateTime(d.updatedAt)}
            </span>
          </div>
        ))}

        {rows.length === 0 && (
          <div style={{ padding: '32px 20px', textAlign: 'center', color: 'var(--muted)', fontSize: 13 }}>Sem notas neste grupo.</div>
        )}
      </Modal>

      <NoteDialog note={note} onClose={() => setNote(null)} />
      {ticketOpen && <TicketModal notes={selectedNotes} onClose={() => setTicketOpen(false)} />}
    </>
  );
}
