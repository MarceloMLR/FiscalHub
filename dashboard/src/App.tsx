import { useState } from 'react';
import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Divider from '@mui/material/Divider';
import CircularProgress from '@mui/material/CircularProgress';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined';
import BarChartOutlinedIcon from '@mui/icons-material/BarChartOutlined';
import HubOutlinedIcon from '@mui/icons-material/HubOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import LogoutOutlinedIcon from '@mui/icons-material/LogoutOutlined';
import { GroupsPage } from './features/groups/GroupsPage';
import { IntegrationsPage } from './features/integrations/IntegrationsPage';
import { ConnectorsPage } from './features/connectors/ConnectorsPage';
import { LoginPage } from './features/auth/LoginPage';
import { useAuth } from './features/auth/AuthContext';
import { useInfo } from './features/useInfo';

const drawerWidth = 224;

// Nav sem router: troca a view por estado. As telas ainda-nao-feitas ficam desabilitadas.
const nav = [
  { key: 'documents', label: 'Documentos', icon: <DescriptionOutlinedIcon fontSize="small" /> },
  { key: 'integrations', label: 'Integrações', icon: <BoltOutlinedIcon fontSize="small" /> },
  { key: 'connectors', label: 'Conectores', icon: <HubOutlinedIcon fontSize="small" />, adminOnly: true },
  { key: 'metrics', label: 'Métricas', icon: <BarChartOutlinedIcon fontSize="small" />, disabled: true },
  { key: 'settings', label: 'Configurações', icon: <SettingsOutlinedIcon fontSize="small" />, disabled: true },
];

const titles: Record<string, { title: string; subtitle: string }> = {
  documents: { title: 'Documentos', subtitle: 'Notas integradas e seus status' },
  integrations: { title: 'Integrações', subtitle: 'Dispare agora ou agende, e acompanhe as execuções' },
  connectors: { title: 'Conectores', subtitle: 'Como este tenant integra: adapters, ambiente e settings' },
};

// Porteiro: enquanto restaura a sessão, mostra loading; sem usuário, o login; com usuário, o painel.
export default function App() {
  const { user, ready } = useAuth();

  if (!ready) {
    return (
      <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <CircularProgress />
      </Box>
    );
  }

  return user ? <Dashboard /> : <LoginPage />;
}

function Dashboard() {
  const { user, logout } = useAuth();
  const [view, setView] = useState('documents');
  const [menuAnchor, setMenuAnchor] = useState<null | HTMLElement>(null);
  const { data: info } = useInfo();

  const env = info?.environment ?? 'Sandbox';
  const isProd = /produ|production/i.test(env);
  const initial = (user?.name ?? '?').trim().charAt(0).toUpperCase();
  const envFg = isProd ? '#15803d' : '#b45309';
  const envDot = isProd ? '#16a34a' : '#d97706';
  const envBg = isProd ? '#e7f6ec' : '#fdf2e3';

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Drawer
        variant="permanent"
        sx={{
          width: drawerWidth,
          flexShrink: 0,
          '& .MuiDrawer-paper': {
            width: drawerWidth,
            boxSizing: 'border-box',
            borderColor: 'divider',
            bgcolor: '#fff',
            display: 'flex',
            flexDirection: 'column',
          },
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2, px: 2, pt: 2.2, pb: 1.5 }}>
          <Box
            sx={{
              width: 30,
              height: 30,
              borderRadius: 2,
              bgcolor: 'primary.main',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#fff',
              fontWeight: 700,
              fontSize: 15,
            }}
          >
            F
          </Box>
          <Typography variant="subtitle1">FiscalHub</Typography>
        </Box>

        <List sx={{ px: 1 }}>
          {nav.filter((item) => !item.adminOnly || user?.role === 'Admin').map((item) => {
            const active = view === item.key;
            return (
              <ListItemButton
                key={item.key}
                selected={active}
                disabled={item.disabled}
                onClick={() => setView(item.key)}
                sx={{ borderRadius: 2, mb: 0.5 }}
              >
                <ListItemIcon sx={{ minWidth: 34, color: active ? 'primary.main' : 'text.secondary' }}>
                  {item.icon}
                </ListItemIcon>
                <ListItemText
                  primary={item.label}
                  primaryTypographyProps={{
                    fontSize: 14,
                    fontWeight: active ? 600 : 400,
                    color: active ? 'primary.main' : 'text.primary',
                  }}
                />
              </ListItemButton>
            );
          })}
        </List>

        <Box sx={{ mt: 'auto', p: 1.5 }}>
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 1.2,
              px: 1.5,
              py: 1.2,
              borderRadius: 2,
              bgcolor: envBg,
            }}
          >
            <Box sx={{ width: 9, height: 9, borderRadius: '50%', bgcolor: envDot, flexShrink: 0 }} />
            <Box>
              <Typography variant="caption" sx={{ color: envFg, display: 'block', lineHeight: 1.2, opacity: 0.85 }}>
                Ambiente
              </Typography>
              <Typography variant="body2" sx={{ fontWeight: 700, color: envFg, lineHeight: 1.2 }}>
                {env}
              </Typography>
            </Box>
          </Box>
        </Box>
      </Drawer>

      <Box component="main" sx={{ flex: 1, minWidth: 0 }}>
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            px: 3,
            py: 1.5,
            borderBottom: 1,
            borderColor: 'divider',
            bgcolor: '#fff',
          }}
        >
          <Box>
            <Typography variant="h6">{titles[view].title}</Typography>
            <Typography variant="body2" color="text.secondary">
              {titles[view].subtitle}
            </Typography>
          </Box>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {user?.name}
            </Typography>
            <IconButton onClick={(e) => setMenuAnchor(e.currentTarget)} sx={{ p: 0 }} aria-label="Conta">
              <Box
                sx={{
                  width: 34,
                  height: 34,
                  borderRadius: '50%',
                  bgcolor: 'var(--accent-tint)',
                  color: 'var(--accent)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontWeight: 600,
                  fontSize: 14,
                }}
              >
                {initial}
              </Box>
            </IconButton>
            <Menu
              anchorEl={menuAnchor}
              open={Boolean(menuAnchor)}
              onClose={() => setMenuAnchor(null)}
              anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
              transformOrigin={{ vertical: 'top', horizontal: 'right' }}
            >
              <Box sx={{ px: 2, py: 1 }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  {user?.name}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {user?.email}
                </Typography>
              </Box>
              <Divider />
              <MenuItem
                onClick={() => {
                  setMenuAnchor(null);
                  logout();
                }}
              >
                <ListItemIcon>
                  <LogoutOutlinedIcon fontSize="small" />
                </ListItemIcon>
                Sair
              </MenuItem>
            </Menu>
          </Box>
        </Box>
        {view === 'integrations' ? (
          <IntegrationsPage />
        ) : view === 'connectors' ? (
          <ConnectorsPage />
        ) : (
          <GroupsPage />
        )}
      </Box>
    </Box>
  );
}
