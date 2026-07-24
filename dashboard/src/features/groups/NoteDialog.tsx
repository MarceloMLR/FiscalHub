import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import DownloadOutlinedIcon from '@mui/icons-material/DownloadOutlined';
import { DocumentDetail } from '../documents/DocumentDetail';
import { api } from '../../api/client';
import type { DocumentSummary } from '../../types';

export function NoteDialog({ note, onClose }: { note: DocumentSummary | null; onClose: () => void }) {
  return (
    <Dialog open={Boolean(note)} onClose={onClose} maxWidth="sm" fullWidth>
      {note && (
        <>
          <DialogTitle sx={{ fontSize: 16, fontWeight: 600 }}>Detalhes da nota</DialogTitle>
          <DialogContent dividers sx={{ p: 0 }}>
            {note.reason && (
              <div style={{ padding: '12px 16px', background: '#fdeaea', color: '#c81e1e', fontSize: 13 }}>
                {note.reason}
              </div>
            )}
            <DocumentDetail doc={note} />
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Fechar</Button>
            <Button
              variant="outlined"
              startIcon={<DownloadOutlinedIcon />}
              component="a"
              href={api.downloadUrl(note.tenantId, note.naturalKey)}
            >
              Baixar arquivos
            </Button>
          </DialogActions>
        </>
      )}
    </Dialog>
  );
}
