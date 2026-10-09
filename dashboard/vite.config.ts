/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server na 5173; consome a API do host (VITE_API_BASE_URL, default http://localhost:5200).
export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
  // O Vitest troca todo CSS por string vazia, até o `?raw`. O palette.test.ts lê o tokens.css para conferir o theme.ts
  // contra ele, então só esse arquivo passa pelo CSS de verdade nos testes.
  test: { css: { include: [/tokens\.css/] } },
});
