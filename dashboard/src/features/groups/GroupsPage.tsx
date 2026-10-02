import { useMemo, useState, type CSSProperties } from 'react';
import Paper from '@mui/material/Paper';
import { type GridColDef } from '@mui/x-data-grid';
import { FhDataGrid } from '../../components/FhDataGrid';
import { DateInput, NativeSelect, Segmented } from '../../components/Controls';
import { useGroups, useGroupTotals } from './useGroups';
import { groupStatus, GroupStatusChip } from './GroupStatusChip';
import { GroupModal } from './GroupModal';
import { formatCompany } from './companyCode';
import { PERIODS, customRangeProblem, periodWindow } from './period';
import { cardSums, modelLabel, modelOptions } from './cards';
import type { DocumentGroup } from '../../types';

const cardStyle: CSSProperties = {
  background: 'var(--surface)',
  border: '1px solid var(--border)',
  borderRadius: 10,
  boxShadow: 'var(--shadow-card)',
};

// Modo/gatilho da integração → rótulo do usuário. Automatic = entrou sem ação humana (coletor, drop, evento).
const TRIGGER_LABEL: Record<string, string> = {
  Automatic: 'Automática',
  Manual: 'Imediata',
  ScheduledDaily: 'Diária (D-1)',
  ScheduledOnce: 'Agendada',
};
const triggerLabel = (t: string) => TRIGGER_LABEL[t] ?? 'Automática';

// O valor do filtro "todos os modelos" no select (o modelo nulo é o filtro desligado).
const ALL_MODELS = '__all__';

// A opção do período personalizado, ao lado das janelas fixas (conferência na tela, 2026-10-02).
const CUSTOM = 'custom';
type PeriodChoice = number | typeof CUSTOM;

// O grupo é (empresa, filial, dia, tipo, modelo, modo): no mesmo dia, a NF-e e a NFS-e ignorada do mesmo estabelecimento são
// linhas distintas.
const rowId = (g: DocumentGroup) => `${g.companyCode}:${g.branchCode}:${g.referenceDate}:${g.type}:${g.model ?? ''}:${g.trigger}`;

const columns: GridColDef<DocumentGroup>[] = [
  {
    field: 'companyCode',
    headerName: 'Empresa',
    flex: 1,
    minWidth: 140,
    headerClassName: 'fhFirstCol',
    cellClassName: 'fhFirstCol',
    // A máscara é só de exibição; o filtro da grade casa pelo texto mostrado, e a URL do grupo usa os dígitos.
    valueGetter: (_v, row) => formatCompany(row.companyCode),
  },
  { field: 'branchCode', headerName: 'Filial', width: 90 },
  { field: 'referenceDate', headerName: 'Data', width: 120 },
  { field: 'model', headerName: 'Modelo', width: 90, valueGetter: (_v, row) => row.model ?? '—' },
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
        <div style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.01em', color }}>
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

  // Os cards contam as notas cuja data de referência está na janela escolhida, e do modelo escolhido. É de propósito: a
  // data de referência é a data fiscal, no fuso de quem emitiu, sem conversão, e a janela é a dos últimos N dias pelo dia
  // do navegador (hoje e os N−1 anteriores), ou o período personalizado, de uma data a outra. A nota processada hoje com
  // data fiscal fora da janela fica fora: as de 2016 do fiscosysdev ficam fora das janelas fixas, e isso é o correto, e não
  // defeito. A ignorada conta em "Documentos", e não em "Com erro" (spec document-grouping). A contagem vem do servidor,
  // sobre todas as notas da janela, e não dos 200 grupos da tabela, que segue mostrando o histórico completo, sem os filtros
  // dos cards.
  const [choice, setChoice] = useState<PeriodChoice>(PERIODS[0].days);
  const [custom, setCustom] = useState(() => periodWindow(PERIODS[0].days, new Date()));
  const [model, setModel] = useState<string | null>(null);
  const preset = choice === CUSTOM ? null : (PERIODS.find((p) => p.days === choice) ?? PERIODS[0]);
  const range = preset ? periodWindow(preset.days, new Date()) : custom;
  const problem = preset ? null : customRangeProblem(custom.from, custom.to);
  const totals = useGroupTotals(range.from, range.to, problem === null);
  // O personalizado começa com a janela que estava escolhida, para a troca não zerar as datas.
  const choosePeriod = (next: PeriodChoice) => {
    if (next === CUSTOM && preset) {
      setCustom(periodWindow(preset.days, new Date()));
    }
    setChoice(next);
  };
  const counts = cardSums(totals.data ?? [], model);
  const models = modelOptions(totals.data ?? [], model);
  const pct = counts.total > 0 ? ((counts.finalizadas / counts.total) * 100).toFixed(1).replace('.', ',') : '0,0';

  return (
    <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', gap: 16, padding: '24px 28px' }}>
      {totals.isError && (
        <div style={{ background: 'var(--error-bg)', border: '1px solid var(--error-border)', color: 'var(--error-text)', borderRadius: 8, padding: '10px 13px', fontSize: 13 }}>
          Falha ao carregar os cards: {(totals.error as Error)?.message}.
        </div>
      )}
      {isError && (
        <div style={{ background: 'var(--error-bg)', border: '1px solid var(--error-border)', color: 'var(--error-text)', borderRadius: 8, padding: '10px 13px', fontSize: 13 }}>
          Falha ao carregar: {(error as Error)?.message}. O host está rodando na 5200?
        </div>
      )}

      {/* Os filtros dos cards: o período (o dia, por padrão, ou o personalizado) e o modelo. A tabela abaixo não os segue. */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <Segmented<PeriodChoice>
          value={choice}
          onChange={choosePeriod}
          options={[...PERIODS.map((p) => ({ value: p.days as PeriodChoice, label: p.label })), { value: CUSTOM, label: 'Personalizado' }]}
        />
        {choice === CUSTOM && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <DateInput label="De" value={custom.from} onChange={(from) => setCustom((c) => ({ ...c, from }))} invalid={problem !== null} />
            <span style={{ color: 'var(--muted)', fontSize: 13 }}>até</span>
            <DateInput label="Até" value={custom.to} onChange={(to) => setCustom((c) => ({ ...c, to }))} invalid={problem !== null} />
          </div>
        )}
        <div style={{ width: 180 }}>
          <NativeSelect value={model ?? ALL_MODELS} onChange={(v) => setModel(v === ALL_MODELS ? null : v)}>
            <option value={ALL_MODELS}>Todos os modelos</option>
            {models.map((m) => (
              <option key={m} value={m}>{modelLabel(m)}</option>
            ))}
          </NativeSelect>
        </div>
        {problem && <span style={{ fontSize: 12.5, color: 'var(--error-text)' }}>{problem}</span>}
      </div>

      {/* KPIs — a janela e o modelo escolhidos */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 14 }}>
        <Kpi label="Documentos" value={counts.total} color="var(--ink)" note={preset ? preset.note : 'no período escolhido'} />
        <div style={{ ...cardStyle, padding: '16px 18px' }}>
          <div className="fh-label" style={{ fontSize: 10.5, whiteSpace: 'nowrap' }}>Finalizados</div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginTop: 7 }}>
            <div style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.01em', color: 'var(--ok-text)' }}>{counts.finalizadas}</div>
            <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--ok-text)' }}>{pct}%</div>
          </div>
          <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 3 }}>confirmados pelo compliance</div>
        </div>
        <Kpi label="Em processamento" value={counts.emProcessamento} color="var(--info-text)" note="aguardando retorno do compliance" />
        <Kpi label="Com erro" value={counts.comErro} color="var(--error-text)" note="rejeitados, sem retorno ou falha" />
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
