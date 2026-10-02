// A empresa de um grupo, para exibição. A do D365 é o CNPJ completo do estabelecimento próprio, sem a pontuação e com as
// letras, e ganha a máscara pelo tamanho (14 caracteres), e não pelo tipo do caractere: o CNPJ alfanumérico tem a mesma
// máscara. A do caminho de XML (8 dígitos) aparece como está. Só apresentação: a URL do grupo, o filtro e o que vai à
// integração usam o código sem máscara.
export function formatCompany(code: string): string {
  return code.length === 14
    ? `${code.slice(0, 2)}.${code.slice(2, 5)}.${code.slice(5, 8)}/${code.slice(8, 12)}-${code.slice(12)}`
    : code;
}
