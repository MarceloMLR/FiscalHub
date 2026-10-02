import type { ReactNode } from 'react';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';

// Os controles de formulário do dashboard, compartilhados pela tela de integrações e pelos filtros dos cards.

// Seletor segmentado: uma escolha entre poucas opções, sempre à vista.
export function Segmented<T extends string | number>({
  value,
  onChange,
  options,
}: {
  value: T;
  onChange: (v: T) => void;
  options: { value: T; label: string }[];
}) {
  return (
    <div style={{ display: 'inline-flex', padding: 3, background: 'var(--surface-sunken)', borderRadius: 8, alignSelf: 'flex-start' }}>
      {options.map((o) => {
        const active = value === o.value;
        return (
          <div
            key={String(o.value)}
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

// A data nativa (aaaa-mm-dd), com a mesma caixa do select.
export function DateInput({
  value,
  onChange,
  label,
  invalid,
}: {
  value: string;
  onChange: (v: string) => void;
  label: string;
  invalid?: boolean;
}) {
  return (
    <input
      type="date"
      aria-label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      style={{
        height: 32,
        padding: '0 9px',
        fontSize: 13,
        color: 'var(--ink)',
        background: 'var(--surface)',
        border: `1px solid ${invalid ? 'var(--error-border)' : 'var(--border-strong)'}`,
        borderRadius: 7,
        outline: 'none',
        boxSizing: 'border-box',
      }}
    />
  );
}

// O select nativo, com a seta do tema.
export function NativeSelect({
  value,
  onChange,
  disabled,
  children,
}: {
  value: string;
  onChange: (v: string) => void;
  disabled?: boolean;
  children: ReactNode;
}) {
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
