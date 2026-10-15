import type { InfoResponse } from '../types';

// Quem vê as fotos cruas — o "Visualizar JSON" e o "Baixar arquivos" do detalhe da nota. A decisão é do servidor, e não
// da tela (ADR-0039 §5; change connector-navigation-and-admin-json, D7): a lista de papéis mora num lugar só no servidor,
// e o /info responde, para o usuário logado, se ele pode. A tela não guarda lista nenhuma e não olha o papel do JWT —
// senão a decisão nasceria de novo aqui, e um papel de Suporte acrescentado no servidor não chegaria aos botões.
//
// Antes de o /info responder, ou se ele falhou (undefined), nada aparece — como o selo de ambiente, que não afirma
// ambiente nenhum antes da resposta. Um botão que promete o que o servidor vai recusar com 403 é promessa quebrada.
export function canViewRawJson(info: InfoResponse | undefined): boolean {
  return info?.canViewRawJson === true;
}
