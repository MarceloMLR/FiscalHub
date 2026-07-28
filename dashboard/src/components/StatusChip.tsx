import type { ReactNode } from 'react';

// Tons do design v3 — cada um mapeia um trio de tokens (bg · border · text) do tokens.css.
export type Tone = 'ok' | 'info' | 'pending' | 'error' | 'warn' | 'dead' | 'partial';

/** Selo de status "ponto + borda" do design v3, colorido por token (claro/escuro juntos). */
export function StatusChip({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        verticalAlign: 'middle',
        gap: 6,
        fontSize: 12,
        lineHeight: 1, // trava a altura: não herda o line-height alto da célula do DataGrid
        fontWeight: 600,
        padding: '3px 9px 3px 7px',
        borderRadius: 6,
        background: `var(--${tone}-bg)`,
        border: `1px solid var(--${tone}-border)`,
        color: `var(--${tone}-text)`,
        whiteSpace: 'nowrap',
      }}
    >
      <span style={{ width: 5, height: 5, borderRadius: 999, background: `var(--${tone}-text)` }} />
      {children}
    </span>
  );
}
