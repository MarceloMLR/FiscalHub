// Apresentação da leitura do desfecho (spec platform-response-trace, design D8 da change establishment-and-readable-dashboard).
// A lista de campos e as omissões vêm prontas do servidor (GET /documents/{tenant}/{chave}/reading); aqui só se humaniza o
// caminho, sem dicionário de campos de um destino, e se cai para o reason quando a leitura não vem.

// itens[0].Item.TipoItem → "item 1 › Item › TipoItem": o índice contado a partir de 1, e os segmentos separados.
export function humanizePath(path: string): string {
  return path
    .split('.')
    .map((segment) => {
      const indexed = /^(.*)\[(\d+)\]$/.exec(segment);
      return indexed ? `item ${Number(indexed[2]) + 1}` : segment;
    })
    .join(' › ');
}

// Na nota aceita, o reason só pode ser a ressalva ("Enviado sem: a; b"): a queda quando a leitura não vem.
export function omissionsFromReason(reason: string): string[] {
  return reason.replace(/^Enviado sem:\s*/, '').split('; ').filter((o) => o.length > 0);
}
