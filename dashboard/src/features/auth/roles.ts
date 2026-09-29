// Quem vê as fotos cruas: o "Visualizar JSON" e o "Baixar arquivos" do detalhe da nota. Hoje só o Admin.
// A lista anda junto com a do servidor (rawTraceRoles, em src/FiscalHub.Host/Program.cs), que é quem manda: o /trace e o
// zip dão 403 para os demais papéis. Um papel de Suporte, quando existir, entra nas duas listas e no UserRole (ADR-0030).
export const RAW_JSON_ROLES: readonly string[] = ['Admin'];

export const canViewRawJson = (role?: string) => role !== undefined && RAW_JSON_ROLES.includes(role);
