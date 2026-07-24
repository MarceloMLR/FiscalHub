import { useState } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Alert from '@mui/material/Alert';
import Typography from '@mui/material/Typography';
import LinearProgress from '@mui/material/LinearProgress';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useGroups } from './useGroups';
import { GroupStatusChip } from './GroupStatusChip';
import { GroupModal } from './GroupModal';
import type { DocumentGroup } from '../../types';

const columns: GridColDef<DocumentGroup>[] = [
  {
    field: 'companyCode',
    headerName: 'Empresa',
    flex: 1,
    minWidth: 150,
    renderCell: (params) => (
      <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, monospace' }}>{params.value as string}</span>
    ),
  },
  { field: 'branchCode', headerName: 'Filial', width: 90 },
  { field: 'referenceDate', headerName: 'Data', width: 110 },
  // Hoje toda ingestao e por evento (realtime). Quando entrar o agendador, o "Tipo" refletira o
  // modo (Em Tempo Real / Agendada / D-1...) e o "Periodo" mostrara inicio-fim das agendadas.
  { field: 'mode', headerName: 'Tipo', width: 140, sortable: false, renderCell: () => 'Em Tempo Real' },
  {
    field: 'periodo',
    headerName: 'Período',
    flex: 1,
    minWidth: 140,
    sortable: false,
    renderCell: () => <span style={{ color: '#9aa1ab' }}>—</span>,
  },
  {
    field: 'total',
    headerName: 'Processadas',
    width: 130,
    sortable: false,
    renderCell: (params) => `${params.row.finalizadas} / ${params.row.total}`,
  },
  {
    field: 'status',
    headerName: 'Status',
    width: 170,
    sortable: false,
    renderCell: (params) => <GroupStatusChip group={params.row} />,
  },
];

function Kpi({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <Paper elevation={0} sx={{ p: 2, borderRadius: 2.5, border: 1, borderColor: 'divider' }}>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography sx={{ fontSize: 26, fontWeight: 600, color, mt: 0.5, fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </Typography>
    </Paper>
  );
}

export function GroupsPage() {
  const { data, isLoading, isError, error } = useGroups();
  const [group, setGroup] = useState<DocumentGroup | null>(null);
  const groups = data ?? [];
  const sum = (pick: (g: DocumentGroup) => number) => groups.reduce((acc, g) => acc + pick(g), 0);

  return (
    <Box sx={{ p: 3 }}>
      {isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          Falha ao carregar: {(error as Error)?.message}. O host está rodando na 5200?
        </Alert>
      )}

      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 1.5, mb: 2.5 }}>
        <Kpi label="Notas" value={sum((g) => g.total)} color="text.primary" />
        <Kpi label="Finalizadas" value={sum((g) => g.finalizadas)} color="success.main" />
        <Kpi label="Em processamento" value={sum((g) => g.emProcessamento)} color="primary.main" />
        <Kpi label="Com erro" value={sum((g) => g.comErro)} color="error.main" />
      </Box>

      <Paper elevation={0} sx={{ borderRadius: 3, border: 1, borderColor: 'divider', overflow: 'hidden' }}>
        <DataGrid
          rows={groups}
          columns={columns}
          getRowId={(row) => `${row.companyCode}:${row.branchCode}:${row.referenceDate}`}
          loading={isLoading}
          onRowClick={(params) => setGroup(params.row as DocumentGroup)}
          slots={{ loadingOverlay: LinearProgress }}
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

      <GroupModal group={group} onClose={() => setGroup(null)} />
    </Box>
  );
}
