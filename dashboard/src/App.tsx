import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import GroupOutlinedIcon from '@mui/icons-material/GroupOutlined';
import LogoutOutlinedIcon from '@mui/icons-material/LogoutOutlined';
import DarkModeOutlinedIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeOutlinedIcon from '@mui/icons-material/LightModeOutlined';
import RadioButtonCheckedIcon from '@mui/icons-material/RadioButtonChecked';
import { GroupsPage } from './features/groups/GroupsPage';
import { IntegrationsPage } from './features/integrations/IntegrationsPage';
import { ConnectorsPage } from './features/connectors/ConnectorsPage';
import { UsersPage } from './features/users/UsersPage';
import { LoginPage } from './features/auth/LoginPage';
import { useAuth } from './features/auth/AuthContext';
import { useInfo } from './features/useInfo';
import { useThemeMode } from './theme/ThemeModeProvider';

type View = 'documents' | 'integrations' | 'settings' | 'users';

const titles: Record<View, { title: string; subtitle: string }> = {
  documents: { title: 'Documentos', subtitle: 'Notas integradas e seus status' },
  integrations: { title: 'Integrações', subtitle: 'Dispare agora ou agende, e acompanhe as execuções' },
  settings: { title: 'Configurações', subtitle: 'Conector, adapters e ambiente deste tenant' },
  users: { title: 'Usuários', subtitle: 'Quem acessa este tenant e o cadastro do cliente' },
};

// Porteiro: enquanto restaura a sessão, mostra loading; sem usuário, o login; com usuário, o painel.
export default function App() {
  const { user, ready } = useAuth();

  if (!ready) {
    return (
      <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', color: 'var(--muted)' }}>
        Carregando…
      </div>
    );
  }

  return user ? <Dashboard /> : <LoginPage />;
}

function Dashboard() {
  const { user, logout } = useAuth();
  const { mode, toggle } = useThemeMode();
  const { data: info } = useInfo();
  const [view, setView] = useState<View>('documents');
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const isAdmin = user?.role === 'Admin';
  const env = info?.environment ?? 'Sandbox';
  const isProd = /produ|production/i.test(env);
  const realtime = info?.realtime ?? true;
  const initials = (user?.name ?? '?').trim().charAt(0).toUpperCase();

  // Fecha o menu do usuário ao clicar fora.
  useEffect(() => {
    if (!menuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [menuOpen]);

  const current = titles[view];

  return (
    <div style={{ display: 'flex', height: '100vh', overflow: 'hidden', background: 'var(--page)' }}>
      {/* ══ Sidebar ══ */}
      <aside
        style={{
          width: 232,
          flexShrink: 0,
          background: 'var(--surface)',
          borderRight: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          position: 'sticky',
          top: 0,
          height: '100vh',
          boxSizing: 'border-box',
        }}
      >
        {/* Marca */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            height: 73,
            padding: '0 14px',
            boxSizing: 'border-box',
            borderBottom: '1px solid var(--border)',
          }}
        >
          <div
            style={{
              width: 28,
              height: 28,
              borderRadius: 8,
              background: 'var(--accent)',
              color: '#fff',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: 14,
              fontWeight: 700,
              flexShrink: 0,
            }}
          >
            F
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 14, fontWeight: 700, letterSpacing: '-0.015em', color: 'var(--ink)' }}>
              FiscalHub
            </div>
            <div className="fh-mono" style={{ fontSize: 10.5, color: 'var(--muted)' }}>
              {user?.tenantId ?? '—'}
            </div>
          </div>
        </div>

        {/* Navegação */}
        <nav style={{ padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: 2 }}>
          <NavSection>Operação</NavSection>
          <NavItem
            active={view === 'documents'}
            icon={<DescriptionOutlinedIcon sx={{ fontSize: 16 }} />}
            onClick={() => setView('documents')}
          >
            Documentos
          </NavItem>
          <NavItem
            active={view === 'integrations'}
            icon={<BoltOutlinedIcon sx={{ fontSize: 16 }} />}
            onClick={() => setView('integrations')}
          >
            Integrações
          </NavItem>

          {isAdmin && (
            <>
              <NavSection style={{ paddingTop: 14 }}>Administração</NavSection>
              <NavItem
                active={view === 'settings'}
                icon={<SettingsOutlinedIcon sx={{ fontSize: 16 }} />}
                onClick={() => setView('settings')}
              >
                Configurações
              </NavItem>
              <NavItem
                active={view === 'users'}
                icon={<GroupOutlinedIcon sx={{ fontSize: 16 }} />}
                onClick={() => setView('users')}
              >
                Usuários
              </NavItem>
            </>
          )}
        </nav>

        {/* Ambiente ativo */}
        <div style={{ marginTop: 'auto', padding: 12 }}>
          <div
            style={{
              border: '1px solid var(--border)',
              borderRadius: 9,
              padding: '11px 13px',
              display: 'flex',
              flexDirection: 'column',
              gap: 9,
            }}
          >
            <div className="fh-label" style={{ fontSize: 10.5, letterSpacing: '0.09em' }}>
              Ambiente ativo
            </div>
            <EnvPill isProd={isProd} label={isProd ? 'Produção' : 'Sandbox'} />
            {realtime && (
              <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 11.5, color: 'var(--muted)' }}>
                <RadioButtonCheckedIcon sx={{ fontSize: 12 }} />
                Tempo real ligado
              </div>
            )}
          </div>
        </div>
      </aside>

      {/* ══ Main ══ */}
      <div style={{ flex: 1, minWidth: 0, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        {/* Topbar */}
        <header
          style={{
            background: 'var(--surface)',
            borderBottom: '1px solid var(--border)',
            height: 73,
            padding: '0 28px',
            boxSizing: 'border-box',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 24,
            position: 'sticky',
            top: 0,
            zIndex: 5,
          }}
        >
          <div style={{ minWidth: 0 }}>
            <div style={{ fontSize: 17, fontWeight: 700, letterSpacing: '-0.02em', color: 'var(--ink)' }}>
              {current.title}
            </div>
            <div style={{ fontSize: 12.5, color: 'var(--muted)', marginTop: 1 }}>{current.subtitle}</div>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
            {/* Alternador de tema */}
            <button
              type="button"
              onClick={toggle}
              aria-label={mode === 'dark' ? 'Ativar modo claro' : 'Ativar modo escuro'}
              title={mode === 'dark' ? 'Modo claro' : 'Modo escuro'}
              style={{
                width: 34,
                height: 34,
                display: 'grid',
                placeItems: 'center',
                borderRadius: 8,
                border: '1px solid var(--border)',
                background: 'var(--surface)',
                color: 'var(--muted)',
                cursor: 'pointer',
              }}
            >
              {mode === 'dark' ? (
                <LightModeOutlinedIcon sx={{ fontSize: 18 }} />
              ) : (
                <DarkModeOutlinedIcon sx={{ fontSize: 18 }} />
              )}
            </button>

            {/* Usuário + menu */}
            <div ref={menuRef} style={{ position: 'relative' }}>
              <div
                onClick={() => setMenuOpen((v) => !v)}
                style={{ display: 'flex', alignItems: 'center', gap: 9, cursor: 'pointer' }}
              >
                <div style={{ textAlign: 'right' }}>
                  <div style={{ fontSize: 13, fontWeight: 600, lineHeight: 1.3, color: 'var(--ink)' }}>
                    {user?.name}
                  </div>
                  <div style={{ fontSize: 11.5, color: 'var(--muted)', lineHeight: 1.3 }}>{user?.role}</div>
                </div>
                <div
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: 8,
                    background: 'var(--accent)',
                    color: '#fff',
                    display: 'grid',
                    placeItems: 'center',
                    fontSize: 12,
                    fontWeight: 700,
                  }}
                >
                  {initials}
                </div>
              </div>

              {menuOpen && (
                <div
                  style={{
                    position: 'absolute',
                    top: 44,
                    right: 0,
                    width: 224,
                    background: 'var(--surface)',
                    border: '1px solid var(--border)',
                    borderRadius: 10,
                    boxShadow: 'var(--shadow-popover)',
                    overflow: 'hidden',
                    zIndex: 10,
                  }}
                >
                  <div style={{ padding: '12px 14px', borderBottom: '1px solid var(--border)' }}>
                    <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--ink)' }}>{user?.name}</div>
                    <div style={{ fontSize: 11.5, color: 'var(--muted)', marginTop: 1 }}>{user?.email}</div>
                  </div>
                  <div
                    onClick={() => {
                      setMenuOpen(false);
                      logout();
                    }}
                    style={{
                      padding: '9px 14px',
                      display: 'flex',
                      alignItems: 'center',
                      gap: 9,
                      fontSize: 13,
                      fontWeight: 500,
                      color: 'var(--text)',
                      cursor: 'pointer',
                    }}
                  >
                    <LogoutOutlinedIcon sx={{ fontSize: 15, color: 'var(--muted)' }} />
                    Sair
                  </div>
                </div>
              )}
            </div>
          </div>
        </header>

        {/* Conteúdo — área rolável. A topbar fica fixa acima; Documentos preenche a altura
            (autoPageSize ajusta as linhas), as demais telas rolam aqui se passarem da altura. */}
        <div style={{ flex: 1, minHeight: 0, overflow: 'auto', display: 'flex', flexDirection: 'column' }}>
          {view === 'integrations' ? (
            <IntegrationsPage />
          ) : view === 'settings' && isAdmin ? (
            <ConnectorsPage />
          ) : view === 'users' && isAdmin ? (
            <UsersPage />
          ) : (
            <GroupsPage />
          )}
        </div>
      </div>
    </div>
  );
}

function NavSection({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return (
    <div
      style={{
        fontSize: 10.5,
        fontWeight: 700,
        letterSpacing: '0.09em',
        textTransform: 'uppercase',
        color: 'var(--muted)',
        padding: '6px 8px',
        ...style,
      }}
    >
      {children}
    </div>
  );
}

function NavItem({
  active,
  icon,
  onClick,
  children,
}: {
  active: boolean;
  icon: ReactNode;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <div
      onClick={onClick}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 9,
        padding: '7px 9px',
        borderRadius: 7,
        fontSize: 13.5,
        fontWeight: active ? 600 : 500,
        cursor: 'pointer',
        background: active ? 'var(--accent-tint)' : 'transparent',
        color: active ? 'var(--accent)' : 'var(--text)',
      }}
      onMouseEnter={(e) => {
        if (!active) e.currentTarget.style.background = 'var(--surface-2)';
      }}
      onMouseLeave={(e) => {
        if (!active) e.currentTarget.style.background = 'transparent';
      }}
    >
      <span style={{ display: 'grid', placeItems: 'center', color: active ? 'var(--accent)' : 'var(--muted)' }}>
        {icon}
      </span>
      {children}
    </div>
  );
}

function EnvPill({ isProd, label }: { isProd: boolean; label: string }) {
  const t = isProd
    ? { bg: 'var(--ok-bg)', border: 'var(--ok-border)', fg: 'var(--ok-text)', dot: 'var(--ok-text)' }
    : { bg: 'var(--warn-bg)', border: 'var(--warn-border)', fg: 'var(--warn-text)', dot: 'var(--warn-text)' };
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 12,
        fontWeight: 600,
        padding: '3px 9px 3px 7px',
        borderRadius: 6,
        background: t.bg,
        border: `1px solid ${t.border}`,
        color: t.fg,
        whiteSpace: 'nowrap',
        alignSelf: 'flex-start',
      }}
    >
      <span style={{ width: 5, height: 5, borderRadius: 999, background: t.dot }} />
      {label}
    </span>
  );
}
