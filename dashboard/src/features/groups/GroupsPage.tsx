import { useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import { useGroups } from './useGroups';
import { GroupStatusChip } from './GroupStatusChip';
import { GroupModal } from './GroupModal';
import type { DocumentGroup } from '../../types';

// Grade de colunas do export v3 (Empresa · Filial · Data · Tipo · Período · Proc. · Status).
const GRID =
  'minmax(66px,0.9fr) minmax(40px,0.5fr) minmax(76px,0.9fr) minmax(78px,0.95fr) minmax(78px,1fr) minmax(54px,0.6fr) minmax(126px,1.1fr)';
const PAGE_SIZE = 25;

const cardStyle: CSSProperties = {
  background: 'var(--surface)',
  border: '1px solid var(--border)',
  borderRadius: 10,
  boxShadow: 'var(--shadow-card)',
};

function Kpi({ label, value, color, note }: { label: string; value: number; color: string; note: string }) {
  return (
    <div style={{ ...cardStyle, padding: '16px 18px' }}>
      <div className="fh-label" style={{ fontSize: 10.5, whiteSpace: 'nowrap' }}>
        {label}
      </div>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginTop: 7 }}>
        <div
          style={{
            fontSize: 28,
            fontWeight: 700,
            letterSpacing: '-0.03em',
            fontVariantNumeric: 'tabular-nums',
            color,
          }}
        >
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
  const [page, setPage] = useState(0);
  const groups = useMemo(() => data ?? [], [data]);

  const sum = (pick: (g: DocumentGroup) => number) => groups.reduce((acc, g) => acc + pick(g), 0);
  const totalNotas = sum((g) => g.total);
  const finalizadas = sum((g) => g.finalizadas);
  const pct = totalNotas > 0 ? ((finalizadas / totalNotas) * 100).toFixed(1).replace('.', ',') : '0,0';

  const pageCount = Math.max(1, Math.ceil(groups.length / PAGE_SIZE));
  const start = page * PAGE_SIZE;
  const visible = groups.slice(start, start + PAGE_SIZE);

  return (
    <div style={{ padding: '24px 28px 44px', display: 'flex', flexDirection: 'column', gap: 18 }}>
      {isError && (
        <div
          style={{
            background: 'var(--error-bg)',
            border: '1px solid var(--error-border)',
            color: 'var(--error-text)',
            borderRadius: 8,
            padding: '10px 13px',
            fontSize: 13,
          }}
        >
          Falha ao carregar: {(error as Error)?.message}. O host está rodando na 5200?
        </div>
      )}

      {/* KPIs por contagem */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 14 }}>
        <Kpi label="Notas" value={totalNotas} color="var(--ink)" note="no período carregado" />
        <FinalizadasKpi value={finalizadas} pct={pct} />
        <Kpi
          label="Em processamento"
          value={sum((g) => g.emProcessamento)}
          color="var(--info-text)"
          note="aguardando retorno do compliance"
        />
        <Kpi label="Com erro" value={sum((g) => g.comErro)} color="var(--error-text)" note="rejeitadas, sem retorno ou falha" />
      </div>

      {/* Tabela fechada por contagem */}
      <div style={{ ...cardStyle, overflow: 'hidden' }}>
        <div style={{ overflowX: 'auto' }}>
          <div style={{ minWidth: 606 }}>
            {/* Cabeçalho */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: GRID,
                columnGap: 10,
                alignItems: 'center',
                padding: '10px 20px',
                background: 'var(--surface-2)',
                borderBottom: '1px solid var(--border)',
                fontSize: 10.5,
                fontWeight: 700,
                letterSpacing: '0.075em',
                textTransform: 'uppercase',
                color: 'var(--muted)',
              }}
            >
              <div>Empresa</div>
              <div>Filial</div>
              <div>Data</div>
              <div>Tipo</div>
              <div>Período</div>
              <div style={{ textAlign: 'right' }}>Proc.</div>
              <div style={{ textAlign: 'right' }}>Status</div>
            </div>

            {/* Linhas */}
            {isLoading && (
              <div style={{ padding: '28px 20px', textAlign: 'center', color: 'var(--muted)', fontSize: 13 }}>
                Carregando…
              </div>
            )}
            {!isLoading && visible.length === 0 && (
              <div style={{ padding: '28px 20px', textAlign: 'center', color: 'var(--muted)', fontSize: 13 }}>
                Nenhum grupo no período.
              </div>
            )}
            {visible.map((g, i) => (
              <div
                key={`${g.companyCode}:${g.branchCode}:${g.referenceDate}`}
                onClick={() => setGroup(g)}
                style={{
                  display: 'grid',
                  gridTemplateColumns: GRID,
                  columnGap: 10,
                  alignItems: 'center',
                  padding: '12px 20px',
                  borderBottom: i === visible.length - 1 ? 'none' : '1px solid var(--border)',
                  fontSize: 13.5,
                  cursor: 'pointer',
                }}
                onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--surface-2)')}
                onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
              >
                <div style={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', color: 'var(--ink)' }}>
                  {g.companyCode}
                </div>
                <div style={{ color: 'var(--text-secondary)', fontVariantNumeric: 'tabular-nums' }}>{g.branchCode}</div>
                <div style={{ fontSize: 12.5, color: 'var(--text-secondary)', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                  {g.referenceDate}
                </div>
                <div style={{ color: 'var(--text)', whiteSpace: 'nowrap' }}>Tempo real</div>
                <div style={{ color: 'var(--faint)' }}>—</div>
                <div style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', color: 'var(--ink)' }}>
                  {g.finalizadas}
                  <span style={{ color: 'var(--muted)' }}>/{g.total}</span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <GroupStatusChip group={g} />
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Rodapé / paginação */}
        <div
          style={{
            padding: '11px 20px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            borderTop: '1px solid var(--border)',
            background: 'var(--surface-2)',
          }}
        >
          <div style={{ fontSize: 12, color: 'var(--muted)', fontVariantNumeric: 'tabular-nums' }}>
            {groups.length === 0 ? 0 : start + 1}–{Math.min(start + PAGE_SIZE, groups.length)} de {groups.length} grupos ·{' '}
            {PAGE_SIZE} por página
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <PagerButton disabled={page === 0} onClick={() => setPage((p) => Math.max(0, p - 1))}>
              <ChevronLeftIcon sx={{ fontSize: 16 }} />
            </PagerButton>
            <PagerButton disabled={page >= pageCount - 1} onClick={() => setPage((p) => Math.min(pageCount - 1, p + 1))}>
              <ChevronRightIcon sx={{ fontSize: 16 }} />
            </PagerButton>
          </div>
        </div>
      </div>

      <GroupModal group={group} onClose={() => setGroup(null)} />
    </div>
  );
}

// Card "Finalizadas" tem o número + o percentual, ambos no verde de sucesso.
function FinalizadasKpi({ value, pct }: { value: number; pct: string }) {
  return (
    <div style={{ ...cardStyle, padding: '16px 18px' }}>
      <div className="fh-label" style={{ fontSize: 10.5, whiteSpace: 'nowrap' }}>
        Finalizadas
      </div>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, marginTop: 7 }}>
        <div
          style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.03em', fontVariantNumeric: 'tabular-nums', color: 'var(--ok-text)' }}
        >
          {value}
        </div>
        <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--ok-text)', fontVariantNumeric: 'tabular-nums' }}>{pct}%</div>
      </div>
      <div style={{ fontSize: 12, color: 'var(--muted)', marginTop: 3 }}>confirmadas pelo compliance</div>
    </div>
  );
}

function PagerButton({ disabled, onClick, children }: { disabled: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      style={{
        width: 28,
        height: 28,
        borderRadius: 6,
        background: 'var(--surface)',
        border: `1px solid ${disabled ? 'var(--border)' : 'var(--border-strong)'}`,
        color: disabled ? 'var(--faint)' : 'var(--text-secondary)',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        cursor: disabled ? 'not-allowed' : 'pointer',
      }}
    >
      {children}
    </button>
  );
}
