import { useMemo, useState, type CSSProperties } from 'react';
import Paper from '@mui/material/Paper';
import { type GridColDef } from '@mui/x-data-grid';
import { FhDataGrid } from '../../components/FhDataGrid';
import { useGroups } from './useGroups';
import { groupStatus, GroupStatusChip } from './GroupStatusChip';
import { GroupModal } from './GroupModal';
import type { DocumentGroup } from '../../types';

const cardStyle: CSSProperties = {
  background: 'var(--surface)',
  border: '1px solid var(--border)',
  borderRadius: 10,
  boxShadow: 'var(--shadow-card)',
};

// Modo/gatilho da integração → rótulo do usuário.
const TRIGGER_LABEL: Record<string, string> = {
  RealTime: 'Tempo real',
  Manual: 'Imediata',
  ScheduledDaily: 'Diária (D-1)',
  ScheduledOnce: 'Agendada',
};
const triggerLabel = (t: string) => TRIGGER_LABEL[t] ?? 'Tempo real';

function todayIso(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

const rowId = (g: DocumentGroup) => `${g.companyCode}:${g.branchCode}:${g.referenceDate}`;

const columns: GridColDef<DocumentGroup>[] = [
  {
    field: 'companyCode',
    headerName: 'Empresa',
    flex: 1,
    minWidth: 140,
    headerClassName: 'fhFirstCol',
    cellClassName: 'fhFirstCol',
    renderCell: (p) => <span className="fh-mono" style={{ fontWeight: 600, color: 'var(--ink)' }}>{p.value as string}</span>,
  },
  { field: 'branchCode', headerName: 'Filial', width: 90 },
  { field: 'referenceDate', headerName: 'Data', width: 120 },
  // "Tipo" = modo/gatilho da integração do grupo (não o tipo do documento).
  { field: 'trigger', headerName: 'Tipo', width: 130, valueGetter: (_v, row) => triggerLabel(row.trigger) },
  {
    field: 'periodo',
    headerName: 'Período',
    flex: 1,
    minWidth: 130,
    sortable: false,
    filterable: false,
    valueGetter: () => '—',
    renderCell: () => <span style={{ color: 'var(--faint)' }}>—</span>,
  },
  {
    field: 'processadas',
    headerName: 'Processadas',
    width: 130,
    sortable: false,
    filterable: false,
    valueGetter: (_v, row) => `${row.finalizadas}/${row.total}`,
    renderCell: (p) => (
      <span style={{ fontVariantNumeric: 'tabular-nums' }}>
        {p.row.finalizadas}
        <span style={{ color: 'var(--muted)' }}>/{p.row.total}</span>
      </span>
    ),
  },
  {
    field: 'status',
    headerName: 'Status',
    width: 160,
    valueGetter: (_v, row) => groupStatus(row).label, // o filtro/ordenação casa pelo rótulo
    renderCell: (p) => <GroupStatusChip group={p.row} />,
  },
];

function Kpi({ label, value, color, note }: { label: string; value: number; color: string; note: string }) {
  return (
    <div style={{ ...cardStyle, padding: '16px 18px' }}>
      <div className="fh-label" style={{ fontSize: 10.5, whiteSpace: 'nowrap' }}>{label}</div>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginTop: 7 }}>
        <div style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.03em', fontVariantNumeric: 'tabular-nums', color }}>
          {value}
        </div>
      </div>
      <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 3 }}>{note}</div>
    </div>
  );
}

export function GroupsPage() {
  const { data, isLoading, isError, error } = useGroups();
  const [group, setGroup] = useState<DocumentGroup | null>(null);
  const groups = useMemo(() => data ?? [], [data]);

  // Os KPIs refletem só o dia de hoje; a tabela abaixo mostra o histórico completo.
  const today = todayIso();
  const doje = useMemo(() => groups.filter((g) => g.referenceDate === today), [groups, today]);
  const sum = (pick: (g: DocumentGroup) => number) => doje.reduce((acc, g) => acc + pick(g), 0);
  const totalHoje = sum((g) => g.total);
  const finalizadas = sum((g) => g.finalizadas);
  const pct = totalHoje > 0 ? ((finalizadas / totalHoje) * 100).toFixed(1).replace('.', ',') : '0,0';

  return (
    <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', gap: 16, padding: '24px 28px' }}>
      {isError && (
        <div style={{ background: 'var(--error-bg)', border: '1px solid var(--error-border)', color: 'var(--error-text)', borderRadius: 8, padding: '10px 13px', fontSize: 13 }}>
          Falha ao carregar: {(error as Error)?.message}. O host está rodando na 5200?
        </div>
      )}

      {/* KPIs — só o dia de hoje */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 14 }}>
        <Kpi label="Documentos" value={totalHoje} color="var(--ink)" note="no dia de hoje" />
        <div style={{ ...cardStyle, padding: '16px 18px' }}>
          <div className="fh-label" style={{ fontSize: 10.5, whiteSpace: 'nowrap' }}>Finalizados</div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginTop: 7 }}>
            <div style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.03em', fontVariantNumeric: 'tabular-nums', color: 'var(--ok-text)' }}>{finalizadas}</div>
            <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--ok-text)', fontVariantNumeric: 'tabular-nums' }}>{pct}%</div>
          </div>
          <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 3 }}>confirmados pelo compliance</div>
        </div>
        <Kpi label="Em processamento" value={sum((g) => g.emProcessamento)} color="var(--info-text)" note="aguardando retorno do compliance" />
        <Kpi label="Com erro" value={sum((g) => g.comErro)} color="var(--error-text)" note="rejeitados, sem retorno ou falha" />
      </div>

      {/* Histórico — filtro/ordenação nativos por coluna; paginação se ajusta à altura (autoPageSize) */}
      <Paper elevation={0} sx={{ ...cardStyle, borderRadius: '10px', flex: 1, minHeight: 320, overflow: 'hidden' }}>
        <FhDataGrid
          rows={groups}
          columns={columns}
          getRowId={rowId}
          loading={isLoading}
          onRowClick={(p) => setGroup(p.row as DocumentGroup)}
        />
      </Paper>

      <GroupModal group={group} onClose={() => setGroup(null)} />
    </div>
  );
}
