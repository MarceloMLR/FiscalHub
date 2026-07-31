// Resolução da URL base da API em RUNTIME (não em build). É o que permite um único bundle de
// frontend servir todos os clientes: acme.fiscalhub.com fala com a API da ACME,
// tmsa.fiscalhub.com com a da TMSA. Nada de tenant é assado no JS — só o host decide o destino.
//
// Ordem de precedência:
//   1. VITE_API_BASE_URL  — override de build (dev, preview, testes locais).
//   2. window.__FISCALHUB__.apiBase — config injetada por host em runtime (index.html/config.js
//      servido junto de cada subdomínio). É o mecanismo recomendado em produção, porque a URL da
//      API de cada cliente nem sempre segue um padrão rígido.
//   3. Convenção por subdomínio — fallback: <tenant>.fiscalhub.com → https://api.<tenant>.fiscalhub.com
//   4. localhost — desenvolvimento.
declare global {
  interface Window {
    __FISCALHUB__?: { apiBase?: string };
  }
}

export function resolveApiBase(): string {
  const override = import.meta.env.VITE_API_BASE_URL as string | undefined;
  if (override) {
    return override.replace(/\/$/, '');
  }

  const injected = window.__FISCALHUB__?.apiBase;
  if (injected) {
    return injected.replace(/\/$/, '');
  }

  const host = window.location.hostname;
  if (host === 'localhost' || host === '127.0.0.1' || host === '') {
    return 'http://localhost:5200';
  }

  // <tenant>.fiscalhub.com → api.<tenant>.fiscalhub.com (mesma origem lógica, backend por-cliente).
  const [sub, ...rest] = host.split('.');
  const root = rest.join('.');
  return `https://api.${sub}.${root}`;
}
