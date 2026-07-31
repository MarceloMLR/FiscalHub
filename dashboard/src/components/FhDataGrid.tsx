import { DataGrid, type DataGridProps } from '@mui/x-data-grid';
import { ptBR } from '@mui/x-data-grid/locales';

// DataGrid padrão do FiscalHub: estilo v3 (dark-aware), sem toolbar (só o filtro/ordenação nativo
// por coluna), paginação por autoPageSize (linhas por página se ajustam à altura) e as correções de
// borda (última linha e "filler" não repetem a linha do rodapé). Uma linha só na base.
const baseSx = {
  border: 0,
  fontFamily: 'inherit',
  color: 'var(--text)',
  '--DataGrid-rowBorderColor': 'var(--border)',
  '& .MuiDataGrid-columnHeader': { backgroundColor: 'var(--surface-2)' },
  '& .MuiDataGrid-columnHeaderTitle': {
    fontWeight: 700,
    fontSize: 10.5,
    letterSpacing: '0.075em',
    textTransform: 'uppercase',
    color: 'var(--muted)',
  },
  '& .MuiDataGrid-columnHeaders': { borderBottom: '1px solid var(--border)' },
  '& .MuiDataGrid-columnSeparator': { display: 'none' },
  '& .MuiDataGrid-cell': { borderTop: 'none', fontSize: 13.5 },
  '& .MuiDataGrid-row--lastVisible': { '--DataGrid-rowBorderColor': 'transparent' },
  '& .MuiDataGrid-row--lastVisible .MuiDataGrid-cell': { borderBottom: 'none' },
  '& .MuiDataGrid-filler, & .MuiDataGrid-scrollbarFiller': {
    '--DataGrid-rowBorderColor': 'transparent',
    borderTop: 'none',
    borderBottom: 'none',
  },
  '& .fhFirstCol': { paddingLeft: '20px' },
  '& .MuiDataGrid-columnHeader:last-of-type, & .MuiDataGrid-cell:last-of-type': { paddingRight: '20px' },
  '& .MuiDataGrid-cell:focus, & .MuiDataGrid-cell:focus-within, & .MuiDataGrid-columnHeader:focus, & .MuiDataGrid-columnHeader:focus-within':
    { outline: 'none' },
  '& .MuiDataGrid-row:hover': { backgroundColor: 'var(--surface-2)' },
  '& .MuiDataGrid-footerContainer': { borderTop: '1px solid var(--border)' },
  '& .MuiTablePagination-root': { color: 'var(--muted)' },
} as const;

export function FhDataGrid({ sx, ...props }: DataGridProps) {
  const clickable = Boolean(props.onRowClick);
  return (
    <DataGrid
      localeText={ptBR.components.MuiDataGrid.defaultProps.localeText}
      autoPageSize
      disableRowSelectionOnClick
      disableColumnSelector
      disableDensitySelector
      columnHeaderHeight={44}
      rowHeight={52}
      {...props}
      sx={{ ...baseSx, '& .MuiDataGrid-row': { cursor: clickable ? 'pointer' : 'default' }, ...sx }}
    />
  );
}
