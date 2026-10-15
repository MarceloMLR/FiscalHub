import { Fragment, useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import AccountBalanceOutlinedIcon from '@mui/icons-material/AccountBalanceOutlined';
import Inventory2OutlinedIcon from '@mui/icons-material/Inventory2Outlined';
import EventOutlinedIcon from '@mui/icons-material/EventOutlined';
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
import { ReservedModulePage } from './features/modules/ReservedModulePage';
import { navigationGroups, shownItem, type ModuleView } from './features/modules/modules';
import { LoginPage } from './features/auth/LoginPage';
import { useAuth } from './features/auth/AuthContext';
import { useInfo } from './features/useInfo';
import { environmentBadge } from './features/environment';
import { useThemeMode } from './theme/ThemeModeProvider';

// A tela que cada item do eixo das integrações abre, pela `view` que o **item declara** (design D10). Não há cadeia de
// ternários por nome de tela nem tabela de títulos separada: o título e o subtítulo vêm do próprio item, e o item e a
// tela não têm como se desalinhar.
const MODULE_SCREENS: Record<ModuleView, ReactNode> = {
  documents: <GroupsPage />,
  accounting: <ReservedModulePage label="Contábil" />,
  inventory: <ReservedModulePage label="Inventário" />,
  integrations: <IntegrationsPage />,
};

const moduleIcons: Record<ModuleView, ReactNode> = {
  documents: <DescriptionOutlinedIcon sx={{ fontSize: 16 }} />,
  accounting: <AccountBalanceOutlinedIcon sx={{ fontSize: 16 }} />,
  inventory: <Inventory2OutlinedIcon sx={{ fontSize: 16 }} />,
  integrations: <EventOutlinedIcon sx={{ fontSize: 16 }} />,
};

type AdminView = 'settings' | 'users';

// O bloco "Administração" fica **fora do eixo** (design D12): Configurações e Usuários não são de conector nenhum — são
// do tenant, e só para Admin. Continua num bloco próprio, abaixo dos grupos de conector, como antes.
const ADMIN_VIEWS: {
  view: AdminView;
  label: string;
  title: string;
  subtitle: string;
  icon: ReactNode;
  screen: ReactNode;
}[] = [
  {
    view: 'settings',
    label: 'Configurações',
    title: 'Configurações',
    subtitle: 'Conector, adapters e ambiente deste tenant',
    icon: <SettingsOutlinedIcon sx={{ fontSize: 16 }} />,
    screen: <ConnectorsPage />,
  },
  {
    view: 'users',
    label: 'Usuários',
    title: 'Usuários',
    subtitle: 'Quem acessa este tenant e o cadastro do cliente',
    icon: <GroupOutlinedIcon sx={{ fontSize: 16 }} />,
    screen: <UsersPage />,
  },
];

// O que está selecionado: um item do eixo das integrações (pela chave dele, o par conector/módulo) ou uma tela do bloco
// Administração. A chave nula é a primeira entrada, antes de qualquer clique: cai na tela de entrada.
type Selection = { kind: 'module'; key: string | null } | { kind: 'admin'; view: AdminView };

function Loading() {
  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', color: 'var(--muted)' }}>
      Carregando…
    </div>
  );
}

// Porteiro: enquanto restaura a sessão, mostra loading; sem usuário, o login; com usuário, o painel.
export default function App() {
  const { user, ready } = useAuth();

  if (!ready) return <Loading />;

  return user ? <Dashboard /> : <LoginPage />;
}

function Dashboard() {
  const { user, logout } = useAuth();
  const { mode, toggle } = useThemeMode();
  const { data: info, isLoading: infoLoading } = useInfo();
  const [selection, setSelection] = useState<Selection>({ kind: 'module', key: null });
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  // Fecha o menu do usuário ao clicar fora.
  useEffect(() => {
    if (!menuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [menuOpen]);

  const isAdmin = user?.role === 'Admin';
  // O ambiente da instância, como o /info o diz; sem ele, o selo não afirma nenhum (D6).
  const badge = environmentBadge(info?.environment);
  // O selo mostra o estado, e não só o ligado: aparece quando o adapter de entrada varre, verde ligado e vermelho
  // desligado, e some para adapter que não varre. Enquanto o /info não chega (ou se falhou), não afirma nada.
  const scans = info?.inboundScans === true;
  const automatic = info?.automaticIntegration === true;
  const initials = (user?.name ?? '?').trim().charAt(0).toUpperCase();
  // O eixo da barra lateral é o conector: um grupo por conector do tenant, com os módulos ligados nele e o Agendamento
  // de quem agenda. Sem conector que produza item, o grupo que nomeia a ausência, com o Fiscal e o Agendamento: o mesmo
  // conjunto de antes desta mudança.
  //
  // Enquanto o /info **não respondeu**, o `undefined` diz "ainda não se sabe", e a barra lateral não afirma nada sobre
  // a configuração do cliente — nem um conector, nem a ausência dele. Uma **falha** do /info, porém, chega aqui como
  // resposta vazia e volta a nomear a ausência: a aplicação não trata falha do /info em lugar nenhum, e isso é maior
  // que a barra lateral (anotado nos Próximos passos do `docs/STATUS.md`).
  const groups = navigationGroups(infoLoading ? undefined : (info?.connectors ?? []));

  // Sem árvore ainda não há item, nem título, nem tela para abrir: a tela espera a resposta, com o mesmo "Carregando…"
  // do porteiro acima, em vez de adivinhar.
  if (groups.length === 0) return <Loading />;

  // O bloco Administração é só do Admin (D12): um Viewer nunca o seleciona, e o resto é o eixo das integrações.
  const admin = isAdmin && selection.kind === 'admin' ? ADMIN_VIEWS.find((a) => a.view === selection.view) : undefined;
  // O item selecionado que deixa de existir — o módulo que o Admin acabou de desligar, o conector que saiu — cai na
  // tela de entrada, que é o primeiro item do primeiro grupo (D11).
  const item = shownItem(groups, selection.kind === 'module' ? selection.key : null);
  const current = admin ?? item;

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
        {/* Marca: o lockup da Fiscosys (colorido no claro, branco no escuro) + "Hub", e o tenant embaixo */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            height: 73,
            padding: '0 14px',
            boxSizing: 'border-box',
            borderBottom: '1px solid var(--border)',
          }}
        >
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
              <img
                src={mode === 'dark' ? '/brand/logotipo-horizontal-branco.svg' : '/brand/logotipo-horizontal-colorido.svg'}
                alt="Fiscosys"
                width={130}
                height={17}
                style={{ display: 'block', width: 'auto', height: 17 }}
              />
              <span style={{ width: 1, height: 14, background: 'var(--border-strong)' }} />
              <span
                style={{ fontFamily: 'var(--font-display)', fontSize: 14.5, fontWeight: 500, lineHeight: 1, color: 'var(--muted)' }}
              >
                Hub
              </span>
            </div>
            <div className="fh-mono" style={{ fontSize: 10.5, color: 'var(--muted)', marginTop: 4 }}>
              {user?.tenantId ?? '—'}
            </div>
          </div>
        </div>

        {/* Navegação: um grupo por conector, com os módulos dele abaixo. Dois níveis, e nunca três — a integração é
            coluna e filtro dentro da página do módulo, e não item de menu (ADR-0039 §1). */}
        <nav style={{ padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: 2 }}>
          {groups.map((group, index) => (
            <Fragment key={group.key}>
              <NavSection style={index === 0 ? undefined : { paddingTop: 14 }}>{group.label}</NavSection>
              {group.items.map((navItem) => (
                <NavItem
                  key={navItem.key}
                  active={!admin && item.key === navItem.key}
                  icon={moduleIcons[navItem.view]}
                  onClick={() => setSelection({ kind: 'module', key: navItem.key })}
                >
                  {navItem.label}
                </NavItem>
              ))}
            </Fragment>
          ))}

          {isAdmin && (
            <>
              <NavSection style={{ paddingTop: 14 }}>Administração</NavSection>
              {ADMIN_VIEWS.map((a) => (
                <NavItem
                  key={a.view}
                  active={admin?.view === a.view}
                  icon={a.icon}
                  onClick={() => setSelection({ kind: 'admin', view: a.view })}
                >
                  {a.label}
                </NavItem>
              ))}
            </>
          )}
        </nav>

        {/* Ambiente da instância */}
        <div style={{ marginTop: 'auto', padding: 12 }}>
          {(badge || scans) && (
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
              {badge && (
                <>
                  <div className="fh-label" style={{ fontSize: 10.5, letterSpacing: '0.09em' }}>
                    Ambiente ativo
                  </div>
                  <EnvPill isProd={badge.isProd} label={badge.label} />
                </>
              )}
              {scans && (
                <div
                  style={{
                    display: 'flex', alignItems: 'center', gap: 6, fontSize: 11.5, fontWeight: 600,
                    color: automatic ? 'var(--ok-text)' : 'var(--error-text)',
                  }}
                >
                  <RadioButtonCheckedIcon sx={{ fontSize: 12 }} />
                  {automatic ? 'Integração automática ligada' : 'Integração automática desligada'}
                </div>
              )}
            </div>
          )}
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
            <div style={{ fontFamily: 'var(--font-display)', fontSize: 17, fontWeight: 700, letterSpacing: '-0.02em', color: 'var(--ink)' }}>
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
                    color: 'var(--accent-ink)',
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
          {admin ? admin.screen : MODULE_SCREENS[item.view]}
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
        color: active ? 'var(--accent-on-tint)' : 'var(--text)',
      }}
      onMouseEnter={(e) => {
        if (!active) e.currentTarget.style.background = 'var(--surface-2)';
      }}
      onMouseLeave={(e) => {
        if (!active) e.currentTarget.style.background = 'transparent';
      }}
    >
      <span style={{ display: 'grid', placeItems: 'center', color: active ? 'var(--accent-on-tint)' : 'var(--muted)' }}>
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
