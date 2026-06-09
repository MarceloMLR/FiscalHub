using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Inbound;

/// <summary>Filtros de uma busca de documentos na origem (modo pull). Apenas o período é obrigatório.</summary>
public sealed record DiscoveryCriteria
{
    /// <summary>Tenant alvo da busca.</summary>
    public required string TenantId { get; init; }

    /// <summary>Início do período.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>Fim do período.</summary>
    public required DateTimeOffset End { get; init; }

    /// <summary>Companhia.</summary>
    public string? Company { get; init; }

    /// <summary>Estabelecimento.</summary>
    public string? Establishment { get; init; }

    /// <summary>Número de um documento específico. Acompanha <see cref="Series"/>, pois não é único entre séries.</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>Série do documento.</summary>
    public string? Series { get; init; }

    /// <summary>Tipo do documento.</summary>
    public DocumentType? Type { get; init; }
}
