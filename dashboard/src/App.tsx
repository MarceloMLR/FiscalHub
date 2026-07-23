import AppBar from '@mui/material/AppBar';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import Container from '@mui/material/Container';
import Box from '@mui/material/Box';
import { DocumentsPage } from './features/documents/DocumentsPage';

export default function App() {
  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar position="static" elevation={0} color="default" sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Toolbar>
          <Typography variant="h6" sx={{ fontWeight: 800, letterSpacing: -0.5 }}>
            FiscalHub
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ ml: 2 }}>
            Painel de integração
          </Typography>
        </Toolbar>
      </AppBar>
      <Container maxWidth="xl" disableGutters>
        <DocumentsPage />
      </Container>
    </Box>
  );
}
