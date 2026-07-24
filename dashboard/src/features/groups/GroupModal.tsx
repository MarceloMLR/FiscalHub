import { useState } from 'react';
import Box from '@mui/material/Box';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import Table from '@mui/material/Table';
import TableHead from '@mui/material/TableHead';
import TableBody from '@mui/material/TableBody';
import TableRow from '@mui/material/TableRow';
import TableCell from '@mui/material/TableCell';
import Typography from '@mui/material/Typography';
import { useGroupDocuments } from './useGroups';
import { StatusChip } from '../documents/StatusChip';
import { NoteDialog } from './NoteDialog';
import type { DocumentGroup, DocumentSummary } from '../../types';

export function GroupModal({ group, onClose }: { group: DocumentGroup | null; onClose: () => void }) {
  const { data: docs } = useGroupDocuments(group?.companyCode, group?.branchCode, group?.referenceDate);
  const [note, setNote] = useState<DocumentSummary | null>(null);
  const rows = docs ?? [];

  return (
    <Dialog open={Boolean(group)} onClose={onClose} maxWidth="md" fullWidth>
      {group && (
        <>
          <DialogTitle sx={{ pb: 0.5 }}>
            <Typography sx={{ fontSize: 16, fontWeight: 600 }}>
              Empresa {group.companyCode} &middot; Filial {group.branchCode}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {group.referenceDate} &middot; {group.total} notas
            </Typography>
          </DialogTitle>
          <DialogContent>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Número</TableCell>
                  <TableCell>Modelo</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell align="right">Consulta</TableCell>
                  <TableCell align="right">Atualizado</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {rows.map((d) => (
                  <TableRow key={d.naturalKey} hover sx={{ cursor: 'pointer' }} onClick={() => setNote(d)}>
                    <TableCell sx={{ fontFamily: 'ui-monospace, monospace' }}>{d.number ?? d.naturalKey}</TableCell>
                    <TableCell>{d.model ?? '—'}</TableCell>
                    <TableCell><StatusChip status={d.status} /></TableCell>
                    <TableCell align="right">{d.attempts}</TableCell>
                    <TableCell align="right">{new Date(d.updatedAt).toLocaleString('pt-BR')}</TableCell>
                  </TableRow>
                ))}
                {rows.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={5}>
                      <Box sx={{ py: 2, textAlign: 'center', color: 'text.secondary' }}>Sem notas neste grupo.</Box>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </DialogContent>
        </>
      )}
      <NoteDialog note={note} onClose={() => setNote(null)} />
    </Dialog>
  );
}
