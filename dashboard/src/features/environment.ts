// O selo do ambiente da instância (GET /info). O ambiente é da instância, e não do perfil (ADR-0036 §3): o selo diz só o
// que o /info disse. Enquanto ele não chega, ou se falhou, o selo não afirma ambiente nenhum — um padrão aqui é o erro
// que o design proíbe (change platform-account-and-connectors, D6), e um "Sandbox" presumido numa instância de produção
// é exatamente o engano que ele veio apagar.
export interface EnvironmentBadge {
  isProd: boolean;
  label: string;
}

export function environmentBadge(environment: string | undefined): EnvironmentBadge | null {
  switch (environment) {
    case 'Production':
      return { isProd: true, label: 'Produção' };
    case 'Sandbox':
      return { isProd: false, label: 'Sandbox' };
    default:
      return null;
  }
}
