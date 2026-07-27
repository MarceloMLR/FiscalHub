// Schema dos campos de cada adapter. No modelo de produção, isto viria do backend (cada adapter
// declara o próprio schema); aqui fica no front pra simplificar a demo. Segredos entram como
// REFERÊNCIA (kv:...) — o valor real fica no Key Vault, nunca digitado aqui.

export interface AdapterField {
  key: string;
  label: string;
  placeholder?: string;
  reference?: boolean; // campo que guarda uma referência de segredo (kv:...)
}

// Adapters de entrada (ERP) — settings num objeto plano.
export const INBOUND_ADAPTERS: Record<string, AdapterField[]> = {
  Dynamics365: [
    { key: 'url', label: 'URL da instância', placeholder: 'https://empresa.crm.dynamics.com/' },
    { key: 'clientIdRef', label: 'Client ID (referência)', placeholder: 'kv:d365-clientid', reference: true },
    { key: 'clientSecretRef', label: 'Client Secret (referência)', placeholder: 'kv:d365-secret', reference: true },
  ],
  iScala: [
    { key: 'host', label: 'Host', placeholder: 'iscala.cliente.local' },
    { key: 'company', label: 'Empresa (código)', placeholder: 'B01' },
    { key: 'userRef', label: 'Usuário (referência)', placeholder: 'kv:iscala-user', reference: true },
    { key: 'passwordRef', label: 'Senha (referência)', placeholder: 'kv:iscala-pass', reference: true },
  ],
};

// Adapters de saída (compliance) — settings por ambiente (sandbox/production).
export const OUTBOUND_ADAPTERS: Record<string, AdapterField[]> = {
  Avalara: [
    { key: 'baseUrl', label: 'URL base', placeholder: 'https://api.avalara.com/' },
    { key: 'clientSecretRef', label: 'Client Secret (referência)', placeholder: 'kv:avalara-secret', reference: true },
    { key: 'clientTokenRef', label: 'Client Token (referência)', placeholder: 'kv:avalara-token', reference: true },
  ],
  Mock: [{ key: 'baseUrl', label: 'URL base', placeholder: 'http://localhost:5100/' }],
};

export const ENVIRONMENTS = ['Sandbox', 'Production'] as const;
