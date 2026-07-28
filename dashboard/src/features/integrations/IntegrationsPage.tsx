import { useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import AddIcon from '@mui/icons-material/Add';
import CloseIcon from '@mui/icons-material/Close';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { useCompanies, useBranches } from '../manual/useDirectory';
import { useSchedules, useExecutions } from '../schedules/useScheduling';
import { StatusChip } from '../../components/StatusChip';
import type { CreateScheduleRequest, IntegrationModeName, Schedule } from '../../types';

const ALL_BRANCHES = '__all__';
const MODE_LABEL: Record<IntegrationModeName, string> = {
  Manual: 'Imediata',
  ScheduledDaily: 'Diária (D-1)',
  ScheduledOnce: 'Agendada',
};
const AG_GRID = 'minmax(88px,1fr) minmax(74px,0.9fr) minmax(46px,0.6fr) minmax(118px,1fr) 84px 168px';
const EX_GRID = 'minmax(86px,1fr) minmax(74px,0.9fr) minmax(46px,0.6fr) minmax(112px,1.2fr) 56px minmax(104px,1fr)';

type Mode = 'now' | 'daily' | 'once';
type Tab = 'schedules' | 'executions';

const card: CSSProperties = {
  background: 'var(--surface)',
  border: '1px solid var(--border)',
  borderRadius: 10,
  boxShadow: 'var(--shadow-card)',
  overflow: 'hidden',
};

function fmt(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function firstOfPreviousMonth(): string {
  const n = new Date();
  return fmt(new Date(n.getFullYear(), n.getMonth() - 1, 1));
}
function dateTime(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}
// nextRunAt vem em UTC; a UI edita em horário de Brasília (-03:00). Converte pra data/hora local.
function toBrtParts(iso: string): { date: string; time: string } {
  const brt = new Date(new Date(iso).getTime() - 3 * 3600 * 1000);
  const p = (n: number) => String(n).padStart(2, '0');
  return {
    date: `${brt.getUTCFullYear()}-${p(brt.getUTCMonth() + 1)}-${p(brt.getUTCDate())}`,
    time: `${p(brt.getUTCHours())}:${p(brt.getUTCMinutes())}`,
  };
}

export function IntegrationsPage() {
  const qc = useQueryClient();
  const companies = useCompanies();
  const schedules = useSchedules();
  const executions = useExecutions();

  const [tab, setTab] = useState<Tab>('schedules');
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

  const [mode, setMode] = useState<Mode>('now');
  const [scope, setScope] = useState<'period' | 'note'>('period');
  const [company, setCompany] = useState('');
  const [branch, setBranch] = useState(ALL_BRANCHES);
  const [documentNumber, setDocumentNumber] = useState('');
  const [start, setStart] = useState(firstOfPreviousMonth());
  const [end, setEnd] = useState(fmt(new Date()));
  const [timeOfDay, setTimeOfDay] = useState('06:00');
  const [runAt, setRunAt] = useState('');
  const branches = useBranches(company);
  const branchCode = () => (branch === ALL_BRANCHES ? null : branch);

  const activeCount = useMemo(() => (schedules.data ?? []).filter((s) => s.active).length, [schedules.data]);

  const runNow = useMutation({
    mutationFn: () =>
      api.runManualIntegration({
        companyCode: company,
        branchCode: branchCode(),
        documentNumber: scope === 'note' ? documentNumber : null,
        periodStart: `${start}T00:00:00-03:00`,
        periodEnd: `${end}T23:59:59-03:00`,
      }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ['executions'] });
      setBanner(r.discovered > 0 ? `${r.discovered} nota(s) enfileirada(s) — acompanhe em Documentos.` : 'Nenhuma nota encontrada para os filtros.');
      setOpen(false);
    },
  });

  const createSchedule = useMutation({
    mutationFn: () => {
      const body: CreateScheduleRequest =
        mode === 'daily'
          ? { mode: 'ScheduledDaily', companyCode: company, branchCode: branchCode(), timeOfDay }
          : {
              mode: 'ScheduledOnce',
              companyCode: company,
              branchCode: branchCode(),
              runAt: `${runAt}:00-03:00`,
              periodStart: `${start}T00:00:00-03:00`,
              periodEnd: `${end}T23:59:59-03:00`,
            };
      return api.createSchedule(body);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['schedules'] });
      setBanner('Agendamento criado. O host executa no horário marcado.');
      setTab('schedules');
      setOpen(false);
    },
  });

  const scheduleBody = (): CreateScheduleRequest =>
    mode === 'daily'
      ? { mode: 'ScheduledDaily', companyCode: company, branchCode: branchCode(), timeOfDay }
      : {
          mode: 'ScheduledOnce',
          companyCode: company,
          branchCode: branchCode(),
          runAt: `${runAt}:00-03:00`,
          periodStart: `${start}T00:00:00-03:00`,
          periodEnd: `${end}T23:59:59-03:00`,
        };

  const updateSchedule = useMutation({
    mutationFn: () => api.updateSchedule(editingId!, scheduleBody()),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['schedules'] });
      setBanner('Agendamento atualizado.');
      setTab('schedules');
      setOpen(false);
      setEditingId(null);
    },
  });

  const deactivate = useMutation({
    mutationFn: (id: number) => api.deactivateSchedule(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schedules'] }),
  });

  const periodOk = start !== '' && end !== '' && start <= end;
  const pending = runNow.isPending || createSchedule.isPending || updateSchedule.isPending;
  const canSubmit =
    company !== '' &&
    !pending &&
    (mode === 'now'
      ? periodOk && (scope === 'period' || documentNumber.trim() !== '')
      : mode === 'daily'
        ? timeOfDay !== ''
        : runAt !== '' && periodOk);

  const resetMutations = () => {
    runNow.reset();
    createSchedule.reset();
    updateSchedule.reset();
  };

  const openModal = () => {
    resetMutations();
    setBanner(null);
    setEditingId(null);
    setMode('now');
    setOpen(true);
  };

  // Abre o modal já preenchido com os dados do agendamento, em modo edição.
  const openEdit = (s: Schedule) => {
    resetMutations();
    setBanner(null);
    setEditingId(s.id);
    setCompany(s.companyCode);
    setBranch(s.branchCode ?? ALL_BRANCHES);
    const parts = toBrtParts(s.nextRunAt);
    if (s.mode === 'ScheduledDaily') {
      setMode('daily');
      setTimeOfDay(parts.time);
    } else {
      setMode('once');
      setRunAt(`${parts.date}T${parts.time}`);
      if (s.periodStart) setStart(s.periodStart);
      if (s.periodEnd) setEnd(s.periodEnd);
    }
    setOpen(true);
  };

  const isEditing = editingId != null;
  const submit = () => (isEditing ? updateSchedule.mutate() : mode === 'now' ? runNow.mutate() : createSchedule.mutate());
  const errorMsg =
    (runNow.error as Error)?.message ??
    (createSchedule.error as Error)?.message ??
    (updateSchedule.error as Error)?.message;

  return (
    <div style={{ padding: '28px 28px 44px', display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 1040, width: '100%', margin: '0 auto', boxSizing: 'border-box' }}>
      {/* Cabeçalho */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
          <span style={{ fontSize: 26, fontWeight: 700, letterSpacing: '-0.025em', fontVariantNumeric: 'tabular-nums', color: 'var(--ink)' }}>
            {activeCount}
          </span>
          <span style={{ fontSize: 14, color: 'var(--muted)' }}>agendamentos ativos</span>
        </div>
        <button type="button" onClick={openModal} className="fh-btn" style={{ height: 38, padding: '0 16px', fontSize: 14 }}>
          <AddIcon sx={{ fontSize: 17 }} />
          Nova integração
        </button>
      </div>

      {banner && (
        <div style={{ border: '1px solid var(--ok-border)', background: 'var(--ok-bg)', borderRadius: 8, padding: '10px 13px', display: 'flex', gap: 9, alignItems: 'center' }}>
          <CheckCircleOutlineIcon sx={{ fontSize: 16, color: 'var(--ok-text)', flexShrink: 0 }} />
          <div style={{ fontSize: 12.5, color: 'var(--ok-text)' }}>{banner}</div>
        </div>
      )}

      {/* Card com abas */}
      <div style={card}>
        <div style={{ display: 'flex', gap: 24, padding: '16px 22px 0', borderBottom: '1px solid var(--border)' }}>
          <TabButton active={tab === 'schedules'} onClick={() => setTab('schedules')}>
            Agendamentos
          </TabButton>
          <TabButton active={tab === 'executions'} onClick={() => setTab('executions')}>
            Execuções recentes
          </TabButton>
        </div>

        {tab === 'schedules' ? (
          <GridTable grid={AG_GRID} minWidth={570} head={['Tipo', 'Empresa', 'Filial', 'Próximo disparo', 'Status', { label: 'Ação', align: 'right' }]}>
            {(schedules.data ?? []).map((s, i, arr) => (
              <Row key={s.id} grid={AG_GRID} last={i === arr.length - 1}>
                <Cell nowrap>{MODE_LABEL[s.mode]}</Cell>
                <Cell mono strong>{s.companyCode}</Cell>
                <Cell muted mono>{s.branchCode ?? 'Todas'}</Cell>
                <Cell mono nowrap>{dateTime(s.nextRunAt)}</Cell>
                <div>
                  <StatusChip tone={s.active ? 'ok' : 'pending'}>{s.active ? 'Ativo' : 'Inativo'}</StatusChip>
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 6 }}>
                  <button
                    type="button"
                    className="fh-btn fh-btn-secondary"
                    onClick={() => openEdit(s)}
                    style={{ height: 30, padding: '0 12px', fontSize: 13 }}
                  >
                    Editar
                  </button>
                  {s.active && (
                    <button
                      type="button"
                      className="fh-btn-danger"
                      onClick={() => deactivate.mutate(s.id)}
                      disabled={deactivate.isPending}
                      style={{ height: 30, padding: '0 12px', fontSize: 13, fontWeight: 600, borderRadius: 6 }}
                    >
                      Desativar
                    </button>
                  )}
                </div>
              </Row>
            ))}
            {(schedules.data?.length ?? 0) === 0 && <Empty>Nenhum agendamento ainda.</Empty>}
          </GridTable>
        ) : (
          <GridTable grid={EX_GRID} minWidth={570} head={['Modo', 'Empresa', 'Filial', 'Período', { label: 'Notas', align: 'right' }, { label: 'Quando', align: 'right' }]}>
            {(executions.data ?? []).map((e, i, arr) => (
              <Row key={e.id} grid={EX_GRID} last={i === arr.length - 1}>
                <Cell nowrap>{MODE_LABEL[e.mode]}</Cell>
                <Cell mono strong>{e.companyCode}</Cell>
                <Cell muted mono>{e.branchCode ?? 'Todas'}</Cell>
                <Cell mono nowrap>
                  {e.periodStart} → {e.periodEnd}
                </Cell>
                <Cell mono align="right">{e.discoveredCount}</Cell>
                <Cell muted mono align="right" nowrap>{dateTime(e.runAt)}</Cell>
              </Row>
            ))}
            {(executions.data?.length ?? 0) === 0 && <Empty>Nenhuma execução ainda.</Empty>}
          </GridTable>
        )}
      </div>

      {/* Modal Nova integração */}
      {open && (
        <div
          onClick={() => setOpen(false)}
          style={{ position: 'fixed', inset: 0, background: 'rgba(11,18,32,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 32, zIndex: 50 }}
        >
          <div onClick={(e) => e.stopPropagation()} style={{ width: '100%', maxWidth: 620, background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 12, boxShadow: 'var(--shadow-modal)', overflow: 'hidden' }}>
            <div style={{ padding: '18px 22px 14px', borderBottom: '1px solid var(--border)', display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16 }}>
              <div>
                <div style={{ fontSize: 15.5, fontWeight: 700, letterSpacing: '-0.014em', color: 'var(--ink)' }}>
                  {isEditing ? 'Editar agendamento' : 'Nova integração'}
                </div>
                <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', lineHeight: 1.5, marginTop: 3 }}>
                  {mode === 'now'
                    ? 'Dispara agora — um período inteiro ou uma nota pelo número.'
                    : mode === 'daily'
                      ? 'Roda todo dia, processando o dia anterior (D-1).'
                      : 'Roda uma única vez, na data e hora marcadas.'}
                </div>
              </div>
              <button type="button" className="fh-icon-btn fh-icon-btn-ghost" onClick={() => setOpen(false)} aria-label="Fechar" style={{ width: 28, height: 28, flexShrink: 0 }}>
                <CloseIcon sx={{ fontSize: 16 }} />
              </button>
            </div>

            <div style={{ padding: '18px 22px', display: 'flex', flexDirection: 'column', gap: 16 }}>
              <Segmented
                value={mode}
                onChange={setMode}
                options={[
                  ...(isEditing ? [] : [{ value: 'now' as Mode, label: 'Imediata' }]),
                  { value: 'daily', label: 'Diária (D-1)' },
                  { value: 'once', label: 'Agendada' },
                ]}
              />

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(200px, 1fr))', gap: 14 }}>
                <Field label="Empresa">
                  <NativeSelect value={company} onChange={(v) => { setCompany(v); setBranch(ALL_BRANCHES); }} disabled={companies.isLoading}>
                    <option value="" disabled>Selecione…</option>
                    {(companies.data ?? []).map((c) => (
                      <option key={c.code} value={c.code}>{c.code} — {c.name}</option>
                    ))}
                  </NativeSelect>
                </Field>
                <Field label="Filial">
                  <NativeSelect value={branch} onChange={setBranch} disabled={company === '' || branches.isLoading}>
                    <option value={ALL_BRANCHES}>Todas as filiais</option>
                    {(branches.data ?? []).map((b) => (
                      <option key={b.code} value={b.code}>{b.code} — {b.name}</option>
                    ))}
                  </NativeSelect>
                </Field>

                {mode === 'now' && (
                  <Field label="Escopo" span2>
                    <div style={{ display: 'flex', gap: 8 }}>
                      <Radio checked={scope === 'period'} onClick={() => setScope('period')}>Período</Radio>
                      <Radio checked={scope === 'note'} onClick={() => setScope('note')}>Nota específica</Radio>
                    </div>
                  </Field>
                )}

                {mode === 'now' && scope === 'note' && (
                  <Field label="Número da nota">
                    <PrefixInput prefix="nNF" value={documentNumber} onChange={setDocumentNumber} placeholder="456" />
                  </Field>
                )}

                {mode === 'daily' && (
                  <Field label="Horário">
                    <PrefixInput prefix="hora" type="time" value={timeOfDay} onChange={setTimeOfDay} note="Processa sempre o dia anterior." />
                  </Field>
                )}

                {mode === 'once' && (
                  <Field label="Disparar em">
                    <PrefixInput prefix="data" type="datetime-local" value={runAt} onChange={setRunAt} />
                  </Field>
                )}

                {mode !== 'daily' && (
                  <Field label="Período" span2>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <div style={{ flex: 1 }}>
                        <PrefixInput prefix="de" type="date" value={start} onChange={setStart} invalid={!periodOk} />
                      </div>
                      <span style={{ color: 'var(--muted)', fontSize: 13 }}>–</span>
                      <div style={{ flex: 1 }}>
                        <PrefixInput prefix="até" type="date" value={end} onChange={setEnd} invalid={!periodOk} />
                      </div>
                    </div>
                    {!periodOk && <div style={{ fontSize: 12, color: 'var(--error-text)', marginTop: 6 }}>O início deve ser anterior ao fim.</div>}
                  </Field>
                )}
              </div>

              {errorMsg && (runNow.isError || createSchedule.isError || updateSchedule.isError) && (
                <div style={{ border: '1px solid var(--error-border)', background: 'var(--error-bg)', color: 'var(--error-text)', borderRadius: 8, padding: '9px 12px', fontSize: 12.5 }}>
                  Falha: {errorMsg}
                </div>
              )}
            </div>

            <div style={{ padding: '14px 22px', borderTop: '1px solid var(--border)', background: 'var(--surface-2)', display: 'flex', justifyContent: 'flex-end', gap: 9 }}>
              <button type="button" onClick={() => setOpen(false)} className="fh-btn fh-btn-secondary" style={{ height: 32 }}>
                Cancelar
              </button>
              <button type="button" onClick={submit} disabled={!canSubmit} className="fh-btn" style={{ height: 32 }}>
                {pending ? 'Enviando…' : isEditing ? 'Salvar alterações' : mode === 'now' ? 'Integrar agora' : 'Criar agendamento'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/* ── peças ── */

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <div
      onClick={onClick}
      onMouseEnter={(e) => { if (!active) e.currentTarget.style.color = 'var(--text)'; }}
      onMouseLeave={(e) => { if (!active) e.currentTarget.style.color = 'var(--muted)'; }}
      style={{
        fontSize: 15,
        fontWeight: active ? 600 : 500,
        color: active ? 'var(--ink)' : 'var(--muted)',
        paddingBottom: 12,
        borderBottom: active ? '2px solid var(--accent)' : '2px solid transparent',
        marginBottom: -1,
        cursor: 'pointer',
        whiteSpace: 'nowrap',
      }}
    >
      {children}
    </div>
  );
}

type Head = string | { label: string; align: 'right' };
function GridTable({ grid, minWidth, head, children }: { grid: string; minWidth: number; head: Head[]; children: ReactNode }) {
  return (
    <div style={{ overflowX: 'auto' }}>
      <div style={{ minWidth }}>
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: grid,
            columnGap: 10,
            alignItems: 'center',
            padding: '12px 22px',
            background: 'var(--surface-2)',
            borderBottom: '1px solid var(--border)',
            fontSize: 11,
            fontWeight: 700,
            letterSpacing: '0.075em',
            textTransform: 'uppercase',
            color: 'var(--muted)',
          }}
        >
          {head.map((h, i) => {
            const label = typeof h === 'string' ? h : h.label;
            const align = typeof h === 'string' ? 'left' : h.align;
            return (
              <div key={i} style={{ textAlign: align }}>
                {label}
              </div>
            );
          })}
        </div>
        {children}
      </div>
    </div>
  );
}

function Row({ grid, last, children }: { grid: string; last: boolean; children: ReactNode }) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: grid,
        columnGap: 10,
        alignItems: 'center',
        padding: '14px 22px',
        borderBottom: last ? 'none' : '1px solid var(--border)',
        fontSize: 14,
      }}
    >
      {children}
    </div>
  );
}

function Cell({
  children,
  mono,
  strong,
  muted,
  nowrap,
  align,
}: {
  children: ReactNode;
  mono?: boolean;
  strong?: boolean;
  muted?: boolean;
  nowrap?: boolean;
  align?: 'right';
}) {
  return (
    <div
      style={{
        color: strong ? 'var(--ink)' : muted ? 'var(--text-secondary)' : 'var(--text)',
        fontWeight: strong ? 600 : 400,
        fontVariantNumeric: mono ? 'tabular-nums' : undefined,
        whiteSpace: nowrap ? 'nowrap' : undefined,
        textAlign: align,
      }}
    >
      {children}
    </div>
  );
}

function Empty({ children }: { children: ReactNode }) {
  return <div style={{ padding: '28px 20px', textAlign: 'center', color: 'var(--muted)', fontSize: 13 }}>{children}</div>;
}

function Field({ label, span2, children }: { label: string; span2?: boolean; children: ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6, gridColumn: span2 ? 'span 2' : undefined }}>
      <label style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--text)' }}>{label}</label>
      {children}
    </div>
  );
}

function NativeSelect({ value, onChange, disabled, children }: { value: string; onChange: (v: string) => void; disabled?: boolean; children: ReactNode }) {
  return (
    <div style={{ position: 'relative' }}>
      <select
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        style={{
          height: 32,
          padding: '0 28px 0 11px',
          fontSize: 13,
          color: 'var(--ink)',
          background: 'var(--surface)',
          border: '1px solid var(--border-strong)',
          borderRadius: 7,
          outline: 'none',
          width: '100%',
          boxSizing: 'border-box',
          appearance: 'none',
          cursor: disabled ? 'default' : 'pointer',
        }}
      >
        {children}
      </select>
      <KeyboardArrowDownIcon sx={{ fontSize: 16, position: 'absolute', right: 8, top: 8, color: 'var(--muted)', pointerEvents: 'none' }} />
    </div>
  );
}

function Radio({ checked, onClick, children }: { checked: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <div
      onClick={onClick}
      onMouseEnter={(e) => { if (!checked) e.currentTarget.style.background = 'var(--surface-2)'; }}
      onMouseLeave={(e) => { if (!checked) e.currentTarget.style.background = 'var(--surface)'; }}
      style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        height: 32,
        padding: '0 11px',
        border: `1px solid ${checked ? 'var(--accent)' : 'var(--border-strong)'}`,
        background: checked ? 'var(--accent-tint)' : 'var(--surface)',
        borderRadius: 7,
        cursor: 'pointer',
        boxSizing: 'border-box',
      }}
    >
      <span
        style={{
          width: 14,
          height: 14,
          borderRadius: 999,
          border: `${checked ? 4 : 1}px solid ${checked ? 'var(--accent)' : 'var(--border-strong)'}`,
          background: 'var(--surface)',
          flexShrink: 0,
          boxSizing: 'border-box',
        }}
      />
      <span style={{ fontSize: 13, fontWeight: checked ? 600 : 500, color: checked ? 'var(--accent)' : 'var(--text)', whiteSpace: 'nowrap' }}>
        {children}
      </span>
    </div>
  );
}

function PrefixInput({
  prefix,
  value,
  onChange,
  type = 'text',
  placeholder,
  note,
  invalid,
}: {
  prefix: string;
  value: string;
  onChange: (v: string) => void;
  type?: string;
  placeholder?: string;
  note?: string;
  invalid?: boolean;
}) {
  return (
    <>
      <div
        style={{
          display: 'flex',
          border: `1px solid ${invalid ? 'var(--error-border)' : 'var(--border-strong)'}`,
          borderRadius: 7,
          overflow: 'hidden',
          background: 'var(--surface)',
        }}
      >
        <span style={{ display: 'flex', alignItems: 'center', padding: '0 9px', background: 'var(--surface-2)', borderRight: '1px solid var(--border)', fontSize: 11.5, fontWeight: 600, color: 'var(--muted)' }}>
          {prefix}
        </span>
        <input
          type={type}
          value={value}
          placeholder={placeholder}
          onChange={(e) => onChange(e.target.value)}
          style={{ height: 32, padding: '0 10px', fontSize: 13, fontVariantNumeric: 'tabular-nums', color: 'var(--ink)', background: 'transparent', border: 'none', outline: 'none', flex: 1, minWidth: 0 }}
        />
      </div>
      {note && <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 6 }}>{note}</div>}
    </>
  );
}

function Segmented({ value, onChange, options }: { value: Mode; onChange: (v: Mode) => void; options: { value: Mode; label: string }[] }) {
  return (
    <div style={{ display: 'inline-flex', padding: 3, background: 'var(--surface-sunken)', borderRadius: 8, alignSelf: 'flex-start' }}>
      {options.map((o) => {
        const active = value === o.value;
        return (
          <div
            key={o.value}
            onClick={() => onChange(o.value)}
            onMouseEnter={(e) => { if (!active) e.currentTarget.style.color = 'var(--ink)'; }}
            onMouseLeave={(e) => { if (!active) e.currentTarget.style.color = 'var(--text-secondary)'; }}
            style={{
              fontSize: 12.5,
              fontWeight: active ? 600 : 500,
              padding: '6px 13px',
              borderRadius: 6,
              background: active ? 'var(--surface)' : 'transparent',
              color: active ? 'var(--ink)' : 'var(--text-secondary)',
              boxShadow: active ? 'var(--shadow-card)' : undefined,
              cursor: 'pointer',
              whiteSpace: 'nowrap',
            }}
          >
            {o.label}
          </div>
        );
      })}
    </div>
  );
}
