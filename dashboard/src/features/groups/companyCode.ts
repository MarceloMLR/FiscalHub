import type { Branch, ExecutionSummary } from '../../types';

// A empresa, para exibição, pelo tamanho, e não pelo tipo do caractere: o CNPJ alfanumérico tem a mesma máscara.
// - 14 caracteres: o CNPJ completo (a empresa do diretório do D365, a matriz, e a dos grupos do D365), como 44.278.225/0001-80;
// - 8 caracteres: a raiz do CNPJ (a empresa dos grupos do caminho de XML e a do diretório de exemplo), como 44.278.225;
// - qualquer outro tamanho: como está.
// Um formatador só, para o dropdown, as tabelas da integração, os cards e o modal (change company-root-in-directory, D6).
// Só apresentação: a URL do grupo, o filtro e o que vai à integração usam o código sem máscara.
export function formatCompany(code: string): string {
  if (code.length === 14) {
    return `${code.slice(0, 2)}.${code.slice(2, 5)}.${code.slice(5, 8)}/${code.slice(8, 12)}-${code.slice(12)}`;
  }

  return code.length === 8 ? `${code.slice(0, 2)}.${code.slice(2, 5)}.${code.slice(5)}` : code;
}

// A empresa na tabela de execuções: o CNPJ do estabelecimento que a descoberta resolveu, quando a execução o gravou; a
// empresa pedida, quando não (várias filiais, o catálogo local, ou a execução de antes do campo). Só o que a execução gravou:
// o histórico não consulta o diretório (change explicit-credential-and-execution-cnpj, D7). Os agendamentos mostram a empresa.
export function formatExecutionCompany(execution: Pick<ExecutionSummary, 'companyCode' | 'establishmentTaxId'>): string {
  return formatCompany(execution.establishmentTaxId || execution.companyCode);
}

// A filial no dropdown: com o CNPJ, ele mascarado e o código ao lado (44.278.225/0002-60 — SP-01); sem ele (o diretório de
// exemplo), o código e o nome. O valor da opção é sempre o código, que é o que o agendamento grava.
export function formatBranch(branch: Branch): string {
  return branch.taxId ? `${formatCompany(branch.taxId)} — ${branch.code}` : `${branch.code} — ${branch.name}`;
}
