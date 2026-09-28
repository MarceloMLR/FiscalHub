import type { DocumentTrace } from '../../types';

// Leitura das fotos da resposta para a primeira vista do detalhe (spec platform-response-trace, design D8 e D9 da change
// establishment-and-readable-dashboard). A tela lê o mapa de erros por campo do ProblemDetails (RFC 9110), um formato
// padrão, e não o contrato da plataforma: não há dicionário de campos de um destino aqui.

export interface FieldError {
  path: string; // o caminho como a plataforma o escreveu (ex.: itens[0].Item.TipoItem)
  label: string; // o caminho legível (ex.: item 1 › Item › TipoItem)
  messages: string[]; // as mensagens da plataforma, como vieram (já em português)
}

const asObject = (v: unknown): Record<string, unknown> | null =>
  v !== null && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : null;

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

// O mapa errors do corpo de um envelope de resposta, uma entrada por caminho, na ordem da resposta. Sem mapa, null.
export function fieldErrors(envelope: unknown): FieldError[] | null {
  const body = asObject(asObject(asObject(envelope)?.response)?.body);
  const errors = asObject(body?.errors);
  if (!errors) return null;
  const list = Object.entries(errors).map(([path, value]) => ({
    path,
    label: humanizePath(path),
    messages: Array.isArray(value) ? value.map(String) : [String(value)],
  }));
  return list.length > 0 ? list : null;
}

// A lista da nota recusada: a da consulta de status quando foi ela que rejeitou (tem o mapa), senão a do envio.
export function rejectionFields(trace: DocumentTrace | undefined): FieldError[] | null {
  return fieldErrors(trace?.responses?.status) ?? fieldErrors(trace?.responses?.submit);
}

// As omissões que o hub declarou no envio (request.omissions da foto do envio). Sem foto ou sem omissão, null.
export function omissionsOf(trace: DocumentTrace | undefined): string[] | null {
  const omissions = asObject(asObject(trace?.responses?.submit)?.request)?.omissions;
  return Array.isArray(omissions) && omissions.length > 0 ? omissions.map(String) : null;
}

// Na nota aceita, o reason só pode ser a ressalva ("Enviado sem: a; b"): a queda quando a foto falta.
export function omissionsFromReason(reason: string): string[] {
  return reason.replace(/^Enviado sem:\s*/, '').split('; ').filter((o) => o.length > 0);
}
