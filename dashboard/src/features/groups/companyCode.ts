// A empresa de um grupo, para exibição. A do D365 é o CNPJ completo do estabelecimento próprio (14 dígitos) e ganha a
// máscara; a do caminho de XML (8 dígitos) aparece como está. Só apresentação: a URL do grupo e o filtro usam os dígitos.
export function formatCompany(code: string): string {
  return /^\d{14}$/.test(code)
    ? `${code.slice(0, 2)}.${code.slice(2, 5)}.${code.slice(5, 8)}/${code.slice(8, 12)}-${code.slice(12)}`
    : code;
}
