using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Linha de rastreio de um documento — a representação de PERSISTÊNCIA, separada do modelo de
/// domínio (imutável). Uma linha por documento, garantida pelo índice único (TenantId, NaturalKey).
/// </summary>
internal sealed class ProcessedDocument
{
    public int Id { get; set; }

    public required string TenantId { get; set; }

    public required string NaturalKey { get; set; }

    public DocumentType Type { get; set; }

    public IntegrationStatus Status { get; set; }

    public string? ExternalId { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
