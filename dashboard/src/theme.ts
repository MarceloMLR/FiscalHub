import { createTheme, type Theme } from '@mui/material/styles';

export type ThemeMode = 'light' | 'dark';

// Paletas do design system v3 (claro e escuro), com o azul da marca Fiscosys. É o espelho MUI do tokens.css: o MUI
// decompõe as cores e não aceita var(--…), então os hexes se repetem aqui, e o palette.test.ts guarda a sincronia.
// O contrastText é o --accent-ink: sem ele, o MUI escolhe branco a partir de 3:1, e no escuro daria 3,05:1.
const palettes = {
  light: {
    primary: { main: '#0072ce', dark: '#005ba9', light: '#e6f0fb', contrastText: '#ffffff' },
    success: { main: '#17864a' },
    warning: { main: '#a86a00' },
    error: { main: '#c0342e' },
    info: { main: '#236c86' },
    background: { default: '#f5f7f9', paper: '#ffffff' },
    divider: '#e3e8ee',
    text: { primary: '#001a72', secondary: '#6b7788' },
  },
  dark: {
    primary: { main: '#3b9ae1', dark: '#6cbaf0', light: '#10263d', contrastText: '#071020' },
    success: { main: '#4cc98a' },
    warning: { main: '#e0ab5c' },
    error: { main: '#f08f8a' },
    info: { main: '#6fc3e0' },
    background: { default: '#0d1117', paper: '#161b22' },
    divider: '#262d37',
    text: { primary: '#fcfcfc', secondary: '#9aa6b6' },
  },
} as const;

/** Tema MUI mapeado no design v3, no modo claro ou escuro. */
export function createAppTheme(mode: ThemeMode): Theme {
  const p = palettes[mode];
  return createTheme({
    palette: { mode, ...p },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: 'Inter, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
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
      MuiTableCell: { styleOverrides: { root: { fontSize: 13, borderColor: p.divider } } },
      MuiOutlinedInput: {
        styleOverrides: {
          root: { borderRadius: 8 },
          // O espelho da regra de autofill do tokens.css. O MUI tem a dele, mais específica que a global: no escuro,
          // pinta o campo preenchido pelo Chrome de #266798 com texto branco.
          input: {
            '&:-webkit-autofill': {
              WebkitBoxShadow: `0 0 0 1000px ${p.background.paper} inset`,
              WebkitTextFillColor: p.text.primary,
              caretColor: p.text.primary,
            },
          },
        },
      },
    },
  });
}
