namespace FiscalHub.Domain.Envelope;

/// <summary>
/// Envelope fino e comum a todos os tipos de documento. É o MÍNIMO que a esteira precisa para
/// tratar qualquer documento de forma uniforme: identidade, tipo, tenant, status e rastreabilidade.
///
/// NÃO carrega o corpo do documento — o corpo (ex.: <see cref="Goods.GoodsInvoice"/>) é buscado
/// à parte, na origem, pelo consumidor da fila (claim-check: o evento avisa com o ID; o hub busca
/// o payload completo).
///
/// Disciplina: manter este envelope mínimo. Se ele engordar com campos de negócio, vira um
/// "god model" pela porta dos fundos.
/// </summary>
public sealed record DocumentEnvelope
{
    /// <summary>Identidade interna do documento no hub (chave técnica).</summary>
    public required Guid Id { get; init; }

    /// <summary>Tipo/modelo do documento, para a esteira rotear.</summary>
    public required DocumentType Type { get; init; }

    /// <summary>Cliente/tenant dono do documento. Chave para resolver o perfil do tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Chave de negócio vinda da origem (ex.: chave de acesso da NF-e, 44 dígitos). Usada para
    /// idempotência/dedup: o mesmo documento não é processado duas vezes.
    /// </summary>
    public required string NaturalKey { get; init; }

    /// <summary>Estado de integração: emitida ou cancelada.</summary>
    public required DocumentStatus Status { get; init; }

    /// <summary>Correlaciona todos os passos do mesmo fluxo nos logs e na telemetria.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Quando o hub recebeu o aviso do documento.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Última atualização de status/processamento (nulo até o primeiro avanço).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
