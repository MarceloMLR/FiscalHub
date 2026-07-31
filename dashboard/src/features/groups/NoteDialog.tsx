import DownloadOutlinedIcon from '@mui/icons-material/DownloadOutlined';
import { Modal } from '../../components/Modal';
import { DocumentDetail } from '../documents/DocumentDetail';
import { api } from '../../api/client';
import type { DocumentSummary } from '../../types';

export function NoteDialog({ note, onClose }: { note: DocumentSummary | null; onClose: () => void }) {
  if (!note) {
    return null;
  }

  return (
    <Modal
      title="Detalhes da nota"
      onClose={onClose}
      maxWidth={640}
      footer={
        <>
          <button type="button" className="fh-btn fh-btn-secondary" onClick={onClose} style={{ height: 32 }}>
            Fechar
          </button>
          <button type="button" className="fh-btn" onClick={() => api.downloadTrace(note.tenantId, note.naturalKey)} style={{ height: 32 }}>
            <DownloadOutlinedIcon sx={{ fontSize: 16 }} />
            Baixar arquivos
          </button>
        </>
      }
    >
      <DocumentDetail doc={note} />
    </Modal>
  );
}
