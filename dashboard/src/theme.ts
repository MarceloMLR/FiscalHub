import { createTheme } from '@mui/material/styles';

// Tema MUI mapeado no design system v3 (acento petróleo, neutros azulados, Manrope).
// Restila todos os componentes MUI de uma vez; os tokens finos vivem em theme/tokens.css.
export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#0b5c7a', dark: '#094a63', light: '#e6f1f6' },
    success: { main: '#17864a' },
    warning: { main: '#a86a00' },
    error: { main: '#c0342e' },
    info: { main: '#236c86' },
    background: { default: '#f5f7f9', paper: '#ffffff' },
    divider: '#e3e8ee',
    text: { primary: '#0b1220', secondary: '#6b7788' },
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily: 'Manrope, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
    fontSize: 13.5,
    h6: { fontWeight: 700, fontSize: 20, letterSpacing: -0.2 },
    subtitle1: { fontWeight: 600, fontSize: 15 },
    subtitle2: { fontWeight: 700, fontSize: 11, letterSpacing: '0.075em', textTransform: 'uppercase' },
    body2: { fontSize: 13.5 },
    caption: { fontSize: 11.5 },
    button: { fontWeight: 600 },
  },
  components: {
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: { root: { textTransform: 'none', borderRadius: 8, fontWeight: 600 } },
    },
    MuiChip: { styleOverrides: { root: { fontWeight: 600 } } },
    MuiTab: { styleOverrides: { root: { textTransform: 'none', fontWeight: 600, minHeight: 42 } } },
    MuiTableCell: { styleOverrides: { root: { fontSize: 13, borderColor: '#e3e8ee' } } },
    MuiOutlinedInput: { styleOverrides: { root: { borderRadius: 8 } } },
  },
});
