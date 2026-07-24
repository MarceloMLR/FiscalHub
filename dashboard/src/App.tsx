import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined';
import BarChartOutlinedIcon from '@mui/icons-material/BarChartOutlined';
import HubOutlinedIcon from '@mui/icons-material/HubOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import { DocumentsPage } from './features/documents/DocumentsPage';
import { useInfo } from './features/useInfo';

const drawerWidth = 224;

const nav = [
  { label: 'Documentos', icon: <DescriptionOutlinedIcon fontSize="small" />, active: true },
  { label: 'Integração manual', icon: <BoltOutlinedIcon fontSize="small" />, active: false },
  { label: 'Métricas', icon: <BarChartOutlinedIcon fontSize="small" />, active: false },
  { label: 'Conectores', icon: <HubOutlinedIcon fontSize="small" />, active: false },
  { label: 'Configurações', icon: <SettingsOutlinedIcon fontSize="small" />, active: false },
];

export default function App() {
  const { data: info } = useInfo();
  const env = info?.environment ?? 'Sandbox';
  const isProd = /produ|production/i.test(env);

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
          },
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2, px: 2, py: 2.2 }}>
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
          {nav.map((item) => (
            <ListItemButton key={item.label} selected={item.active} sx={{ borderRadius: 2, mb: 0.5 }}>
              <ListItemIcon sx={{ minWidth: 34, color: item.active ? 'primary.main' : 'text.secondary' }}>
                {item.icon}
              </ListItemIcon>
              <ListItemText
                primary={item.label}
                primaryTypographyProps={{
                  fontSize: 14,
                  fontWeight: item.active ? 600 : 400,
                  color: item.active ? 'primary.main' : 'text.primary',
                }}
              />
            </ListItemButton>
          ))}
        </List>
      </Drawer>

      <Box component="main" sx={{ flex: 1, minWidth: 0 }}>
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            px: 3,
            py: 2,
            borderBottom: 1,
            borderColor: 'divider',
            bgcolor: '#fff',
          }}
        >
          <Box>
            <Typography variant="h6">Documentos</Typography>
            <Typography variant="body2" color="text.secondary">
              Notas integradas e seus status
            </Typography>
          </Box>
          <Chip
            size="small"
            label={`Ambiente: ${env}`}
            sx={{
              borderRadius: '20px',
              fontWeight: 600,
              bgcolor: isProd ? '#e7f6ec' : '#fdf2e3',
              color: isProd ? '#15803d' : '#b45309',
            }}
          />
        </Box>
        <DocumentsPage />
      </Box>
    </Box>
  );
}
