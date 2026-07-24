import { useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Drawer from '@mui/material/Drawer';
import Alert from '@mui/material/Alert';
import LinearProgress from '@mui/material/LinearProgress';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useDocuments } from './useDocuments';
import { StatusChip } from './StatusChip';
import { DocumentDetail } from './DocumentDetail';
import { KpiCards } from './KpiCards';
import type { DocumentSummary, IntegrationStatus } from '../../types';

const columns: GridColDef<DocumentSummary>[] = [
  {
    field: 'naturalKey',
    headerName: 'Chave',
    flex: 1,
    minWidth: 180,
    renderCell: (params) => (
      <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, monospace' }}>{params.value as string}</span>
    ),
  },
  { field: 'type', headerName: 'Tipo', width: 150 },
  {
    field: 'status',
    headerName: 'Status',
    width: 150,
    renderCell: (params) => <StatusChip status={params.value as IntegrationStatus} />,
  },
  { field: 'attempts', headerName: 'Consultas', width: 110, type: 'number' },
  {
    field: 'updatedAt',
    headerName: 'Atualizado',
    width: 180,
    valueFormatter: (value) => (value ? new Date(value as string).toLocaleString('pt-BR') : ''),
  },
];

export function DocumentsPage() {
  const { data, isLoading, isError, error } = useDocuments();
  const [selected, setSelected] = useState<DocumentSummary | null>(null);
  const docs = data ?? [];

  return (
    <Box sx={{ p: 3 }}>
      {isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          Falha ao carregar: {(error as Error)?.message}. O host está rodando na 5200?
        </Alert>
      )}

      <KpiCards docs={docs} />

      <Paper elevation={0} sx={{ borderRadius: 3, border: 1, borderColor: 'divider', overflow: 'hidden' }}>
        <DataGrid
          rows={docs}
          columns={columns}
          getRowId={(row) => `${row.tenantId}:${row.naturalKey}`}
          loading={isLoading}
          onRowClick={(params) => setSelected(params.row as DocumentSummary)}
          slots={{ loadingOverlay: () => <LinearProgress /> }}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }}
          pageSizeOptions={[25, 50, 100]}
          disableRowSelectionOnClick
          autoHeight
          sx={{
            border: 0,
            '& .MuiDataGrid-columnHeaders': { bgcolor: '#fafbfc' },
            '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 600, color: 'text.secondary', fontSize: 13 },
            '& .MuiDataGrid-cell': { borderColor: 'divider' },
            '& .MuiDataGrid-cell:focus, & .MuiDataGrid-cell:focus-within': { outline: 'none' },
            '& .MuiDataGrid-columnHeader:focus, & .MuiDataGrid-columnHeader:focus-within': { outline: 'none' },
            '& .MuiDataGrid-row:hover': { bgcolor: 'action.hover' },
            '& .MuiDataGrid-footerContainer': { borderColor: 'divider' },
            cursor: 'pointer',
          }}
        />
      </Paper>

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
