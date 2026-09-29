import { useEffect, useRef, type ReactNode } from 'react';
import CloseIcon from '@mui/icons-material/Close';

// Os modais abertos, do de baixo para o de cima. O Esc fecha só o de cima: com o JSON aberto por cima do detalhe da
// nota, um Esc volta ao detalhe, e não fecha os dois.
const openModals: symbol[] = [];

/** Modal do design system v3: overlay + cartão (surface/borda/raio 12/sombra), cabeçalho com título/subtítulo
 *  e X, corpo rolável e rodapé opcional. Fecha no clique fora e no Esc (só o de cima). Dark-aware pelos tokens. */
export function Modal({
  title,
  subtitle,
  onClose,
  footer,
  maxWidth = 620,
  children,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  onClose: () => void;
  footer?: ReactNode;
  maxWidth?: number;
  children: ReactNode;
}) {
  // O lugar na pilha é o da montagem: um onClose novo a cada render do pai não pode trazer o modal para o topo.
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    const id = Symbol('modal');
    openModals.push(id);
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && openModals[openModals.length - 1] === id) onCloseRef.current();
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
      openModals.splice(openModals.indexOf(id), 1);
    };
  }, []);

  return (
    <div
      onClick={onClose}
      style={{ position: 'fixed', inset: 0, background: 'rgba(11,18,32,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 32, zIndex: 50 }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: '100%',
          maxWidth,
          maxHeight: '86vh',
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: 12,
          boxShadow: 'var(--shadow-modal)',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <div style={{ padding: '18px 22px 14px', borderBottom: '1px solid var(--border)', display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 16, flexShrink: 0 }}>
          <div style={{ minWidth: 0 }}>
            <div style={{ fontSize: 15.5, fontWeight: 700, letterSpacing: '-0.014em', color: 'var(--ink)' }}>{title}</div>
            {subtitle && <div style={{ fontSize: 12.5, color: 'var(--muted)', marginTop: 3 }}>{subtitle}</div>}
          </div>
          <button type="button" className="fh-icon-btn fh-icon-btn-ghost" onClick={onClose} aria-label="Fechar" style={{ width: 28, height: 28, flexShrink: 0 }}>
            <CloseIcon sx={{ fontSize: 16 }} />
          </button>
        </div>

        <div style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>{children}</div>

        {footer && (
          <div style={{ padding: '14px 22px', borderTop: '1px solid var(--border)', background: 'var(--surface-2)', display: 'flex', justifyContent: 'flex-end', gap: 9, flexShrink: 0 }}>
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
