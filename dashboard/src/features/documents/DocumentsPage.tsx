import { useState } from 'react';
import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import Alert from '@mui/material/Alert';
import LinearProgress from '@mui/material/LinearProgress';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useDocuments } from './useDocuments';
import { StatusChip } from './StatusChip';
import { DocumentDetail } from './DocumentDetail';
import type { DocumentSummary, IntegrationStatus } from '../../types';

const columns: GridColDef<DocumentSummary>[] = [
  { field: 'naturalKey', headerName: 'Chave', flex: 1, minWidth: 180 },
  { field: 'type', headerName: 'Tipo', width: 150 },
  {
    field: 'status',
    headerName: 'Status',
    width: 150,
    renderCell: (params) => <StatusChip status={params.value as IntegrationStatus} />,
  },
  { field: 'attempts', headerName: 'Consultas', width: 110, type: 'number' },
  { field: 'externalId', headerName: 'GUID externo', width: 150 },
  {
    field: 'updatedAt',
    headerName: 'Atualizado',
    width: 190,
    valueFormatter: (value) => (value ? new Date(value as string).toLocaleString('pt-BR') : ''),
  },
];

export function DocumentsPage() {
  const { data, isLoading, isError, error } = useDocuments();
  const [selected, setSelected] = useState<DocumentSummary | null>(null);

  return (
    <Box sx={{ p: 3 }}>
      {isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          Falha ao carregar: {(error as Error)?.message}. O host está rodando na 5200?
        </Alert>
      )}

      <Box sx={{ height: 640, bgcolor: 'background.paper', borderRadius: 2 }}>
        <DataGrid
          rows={data ?? []}
          columns={columns}
          getRowId={(row) => `${row.tenantId}:${row.naturalKey}`}
          loading={isLoading}
          onRowClick={(params) => setSelected(params.row as DocumentSummary)}
          slots={{ loadingOverlay: LinearProgress }}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }}
          pageSizeOptions={[25, 50, 100]}
          disableRowSelectionOnClick
          sx={{ border: 0, cursor: 'pointer' }}
        />
      </Box>

      <Drawer
        anchor="right"
        open={Boolean(selected)}
        onClose={() => setSelected(null)}
        PaperProps={{ sx: { width: { xs: '100%', sm: 560 } } }}
      >
        {selected && <DocumentDetail doc={selected} />}
      </Drawer>
    </Box>
  );
}
