using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Outbound;

/// <summary>Informações que acompanham um despacho ao destino.</summary>
public sealed record DispatchContext
{
    /// <summary>Tenant dono do documento.</summary>
    public required string TenantId { get; init; }

    /// <summary>Chave natural do documento — nomeia as fotos de rastreabilidade.</summary>
    public required string NaturalKey { get; init; }

    /// <summary>Identificador de correlação do fluxo.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Operação a executar no destino: carga (<see cref="DocumentStatus.Issued"/>) ou cancelamento
    /// (<see cref="DocumentStatus.Cancelled"/>).
    /// </summary>
    public required DocumentStatus Operation { get; init; }
}
