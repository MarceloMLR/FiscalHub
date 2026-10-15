import { useState } from 'react';
import DownloadOutlinedIcon from '@mui/icons-material/DownloadOutlined';
import ConfirmationNumberOutlinedIcon from '@mui/icons-material/ConfirmationNumberOutlined';
import { Modal } from '../../components/Modal';
import { DocumentDetail } from '../documents/DocumentDetail';
import { TicketModal } from '../support/TicketModal';
import { api } from '../../api/client';
import { canViewRawJson } from '../rawJson';
import { useInfo } from '../useInfo';
import type { DocumentSummary } from '../../types';

export function NoteDialog({ note, onClose }: { note: DocumentSummary | null; onClose: () => void }) {
  // Quem vê as fotos cruas é o /info que diz; a tela não decide pelo papel (D7).
  const { data: info } = useInfo();
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
            {/* O zip tem as fotos cruas: só para quem o /info disser (o servidor dá 403 para os demais). Antes de ele
                responder, ou se falhar, o botão não aparece — para papel nenhum. */}
            {canViewRawJson(info) && (
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
