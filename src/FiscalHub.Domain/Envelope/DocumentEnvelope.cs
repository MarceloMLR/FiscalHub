namespace FiscalHub.Domain.Envelope;

/// <summary>
/// Metadados comuns a todos os tipos de documento, usados pela esteira para roteamento e rastreio.
/// Não contém o corpo do documento, que é buscado à parte (claim-check).
/// </summary>
public sealed record DocumentEnvelope
{
    /// <summary>Identificador interno no hub.</summary>
    public required Guid Id { get; init; }

    /// <summary>Tipo do documento.</summary>
    public required DocumentType Type { get; init; }

    /// <summary>Tenant dono do documento.</summary>
    public required string TenantId { get; init; }

    /// <summary>Chave de negócio da origem (ex.: chave de acesso da NF-e). Usada para deduplicação.</summary>
    public required string NaturalKey { get; init; }

    /// <summary>Estado fiscal do documento.</summary>
    public required DocumentStatus Status { get; init; }

    /// <summary>Identificador de correlação do fluxo.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Momento em que o hub recebeu o documento.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Momento da última atualização, ou nulo se ainda não houve.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
