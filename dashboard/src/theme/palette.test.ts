import { describe, expect, it } from 'vitest';
import tokensCss from './tokens.css?raw';
import { createAppTheme, type ThemeMode } from '../theme';

// A paleta mora em dois lugares que precisam andar juntos (spec dashboard-brand-identity, design D1): os tokens do
// tokens.css e o espelho MUI do theme.ts, que só aceita hex. Este teste é o que impede os dois de divergirem, e o que
// prende os contrastes dos dois tokens novos acima de 4,5:1.
const BLOCKS: Record<ThemeMode, string> = { light: ':root', dark: "html[data-theme='dark']" };

function tokensOf(mode: ThemeMode): Record<string, string> {
  const selector = BLOCKS[mode];
  const start = tokensCss.indexOf(`${selector} {`);
  if (start < 0) throw new Error(`o tokens.css não tem o bloco ${selector}`);
  const body = tokensCss.slice(start, tokensCss.indexOf('}', start));
  const tokens: Record<string, string> = {};
  for (const [, name, value] of body.matchAll(/--([\w-]+)\s*:\s*([^;]+);/g)) tokens[name] = value.trim();
  return tokens;
}

function token(mode: ThemeMode, name: string): string {
  const value = tokensOf(mode)[name];
  if (value === undefined) throw new Error(`o tema ${mode} não define --${name} no tokens.css`);
  return value;
}

/** `#ABC` e `#aabbcc` comparam iguais: o MUI devolve o branco calculado como `#fff`. */
function hex(color: string): string {
  const c = color.trim().toLowerCase();
  return /^#[0-9a-f]{3}$/.test(c) ? `#${[...c.slice(1)].map((d) => d + d).join('')}` : c;
}

// Luminância relativa e razão de contraste do WCAG 2.x.
function luminance(color: string): number {
  const c = hex(color);
  if (!/^#[0-9a-f]{6}$/.test(c)) throw new Error(`cor fora do formato #rrggbb: ${color}`);
  const [r, g, b] = [1, 3, 5]
    .map((i) => parseInt(c.slice(i, i + 2), 16) / 255)
    .map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe.each(['light', 'dark'] as const)('a paleta do tema %s', (mode) => {
  const { palette } = createAppTheme(mode);

  it.each([
    ['primary.main', 'accent', () => palette.primary.main],
    ['primary.dark', 'accent-hover', () => palette.primary.dark],
    ['primary.light', 'accent-tint', () => palette.primary.light],
    ['primary.contrastText', 'accent-ink', () => palette.primary.contrastText],
    ['text.primary', 'ink', () => palette.text.primary],
  ])('o %s do theme.ts é o --%s do tokens.css', (_key, name, read) => {
    expect(hex(read())).toBe(hex(token(mode, name)));
  });

  it.each([
    ['accent-ink', 'accent'],
    ['accent-ink', 'accent-hover'],
    ['accent-on-tint', 'accent-tint'],
  ])('o --%s sobre o --%s passa de 4,5:1', (fg, bg) => {
    expect(contrast(token(mode, fg), token(mode, bg))).toBeGreaterThanOrEqual(4.5);
  });
});
