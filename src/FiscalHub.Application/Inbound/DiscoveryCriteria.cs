using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Critérios de uma busca agendada ou manual (modo pull): "ache os documentos deste tenant, neste
/// período, desta companhia/estabelecimento". Usado pela porta de descoberta.
/// </summary>
public sealed record DiscoveryCriteria
{
    public required string TenantId { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    public string? Company { get; init; }

    public string? Establishment { get; init; }

    /// <summary>
    /// Filtro opcional por número específico do documento (integrar uma nota só). Como o número não
    /// é único entre séries, costuma vir acompanhado de <see cref="Series"/>.
    /// </summary>
    public string? DocumentNumber { get; init; }

    /// <summary>Série do documento — desambígua o <see cref="DocumentNumber"/>.</summary>
    public string? Series { get; init; }

    /// <summary>Filtro opcional por tipo de documento.</summary>
    public DocumentType? Type { get; init; }
}
