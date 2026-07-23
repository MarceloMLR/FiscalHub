import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server na 5173; consome a API do host (VITE_API_BASE_URL, default http://localhost:5200).
export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
});
