// Schema dos campos de cada adapter. No modelo de produção, isto viria do backend (cada adapter
// declara o próprio schema); aqui fica no front pra simplificar a demo. Segredos são CAMPOS DE ESCRITA
// (ADR-0027): o valor digitado vai para o servidor, que o grava no cofre e guarda só a referência no
// perfil. A leitura nunca devolve o valor — a tela mostra só se está configurado e quando.

export interface AdapterField {
  // Caminho no JSON das settings; ponto para objeto aninhado (ex.: 'auth.clientId').
  key: string;
  label: string;
  placeholder?: string;
  secret?: boolean; // campo de escrita: senha, sem preenchimento, enviado só quando digitado
}

// Adapters de entrada (ERP). Os campos que a tela não mostra (companies, e da seção poll tudo menos o enabled) são
// preservados ao salvar.
export const INBOUND_ADAPTERS: Record<string, AdapterField[]> = {
  Dynamics365: [
    { key: 'url', label: 'URL do ambiente F&O', placeholder: 'https://empresa.operations.dynamics.com' },
    { key: 'auth.tenantId', label: 'Tenant do Entra ID', placeholder: '00000000-0000-0000-0000-000000000000' },
    { key: 'auth.clientId', label: 'Client ID', placeholder: '00000000-0000-0000-0000-000000000000' },
    { key: 'auth.clientSecret', label: 'Client Secret', secret: true },
  ],
  iScala: [
    { key: 'host', label: 'Host', placeholder: 'iscala.cliente.local' },
    { key: 'company', label: 'Empresa (código)', placeholder: 'B01' },
    { key: 'user', label: 'Usuário', placeholder: 'integracao' },
    { key: 'password', label: 'Senha', secret: true },
  ],
};

// Adapters de entrada que varrem a origem: só eles têm o interruptor "Integração automática", que grava o poll.enabled
// das settings. No backend, varrer é ter um feed de mudanças (IDocumentChangeFeed) registrado para a origem; esta marca
// é o espelho dele na tela, até os schemas virem do backend (ADR-0029).
export const SCANNING_INBOUND_ADAPTERS: ReadonlySet<string> = new Set(['Dynamics365']);

// Adapters de saída (compliance) — settings por ambiente (sandbox/production). Os campos que a tela não
// mostra (establishments…) são preservados ao salvar.
export const OUTBOUND_ADAPTERS: Record<string, AdapterField[]> = {
  Avalara: [
    // Sem a URL do token: o hub a monta pela URL base. Um tokenUrl já gravado continua valendo e volta intacto ao salvar.
    { key: 'baseUrl', label: 'URL base', placeholder: 'https://api.avalara.com/' },
    { key: 'clientId', label: 'Client ID' },
    { key: 'clientSecret', label: 'Client Secret', secret: true },
  ],
  Mock: [{ key: 'baseUrl', label: 'URL base', placeholder: 'http://localhost:5100/' }],
};

export const ENVIRONMENTS = ['Sandbox', 'Production'] as const;

// Adapters com teste de credencial no servidor (IConnectorCredentialTest): só eles ganham o botão "Testar credencial".
export const CREDENTIAL_TEST_ADAPTERS = {
  inbound: new Set(['Dynamics365']),
  outbound: new Set(['Avalara']),
} as const;
