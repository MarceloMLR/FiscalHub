import { useState } from 'react';
import DownloadOutlinedIcon from '@mui/icons-material/DownloadOutlined';
import ConfirmationNumberOutlinedIcon from '@mui/icons-material/ConfirmationNumberOutlined';
import { Modal } from '../../components/Modal';
import { DocumentDetail } from '../documents/DocumentDetail';
import { TicketModal } from '../support/TicketModal';
import { api } from '../../api/client';
import { useAuth } from '../auth/AuthContext';
import { canViewRawJson } from '../auth/roles';
import type { DocumentSummary } from '../../types';

export function NoteDialog({ note, onClose }: { note: DocumentSummary | null; onClose: () => void }) {
  const { user } = useAuth();
  const [ticketOpen, setTicketOpen] = useState(false);

  if (!note) {
    return null;
  }

  return (
    <>
      <Modal
        title="Detalhes da nota"
        onClose={onClose}
        maxWidth={640}
        footer={
          <>
            <button type="button" className="fh-btn fh-btn-secondary" onClick={() => setTicketOpen(true)} style={{ height: 32, marginRight: 'auto' }}>
              <ConfirmationNumberOutlinedIcon sx={{ fontSize: 16 }} />
              Abrir chamado
            </button>
            <button type="button" className="fh-btn fh-btn-secondary" onClick={onClose} style={{ height: 32 }}>
              Fechar
            </button>
            {/* O zip tem as fotos cruas: só para quem tem o papel (o servidor dá 403 para os demais). */}
            {canViewRawJson(user?.role) && (
              <button type="button" className="fh-btn" onClick={() => api.downloadTrace(note.tenantId, note.naturalKey)} style={{ height: 32 }}>
                <DownloadOutlinedIcon sx={{ fontSize: 16 }} />
                Baixar arquivos
              </button>
            )}
          </>
        }
      >
        <DocumentDetail doc={note} />
      </Modal>

      {ticketOpen && <TicketModal notes={[note]} onClose={() => setTicketOpen(false)} />}
    </>
  );
}
