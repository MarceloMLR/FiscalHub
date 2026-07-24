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

    /// <summary>Código da empresa (agrupamento) — raiz do CNPJ ou código interno.</summary>
    public string? CompanyCode { get; set; }

    /// <summary>Código da filial (agrupamento) — ordem do CNPJ ou código interno.</summary>
    public string? BranchCode { get; set; }

    /// <summary>Data de referência (yyyy-MM-dd) usada no agrupamento.</summary>
    public string? ReferenceDate { get; set; }

    /// <summary>Número do documento (nNF).</summary>
    public string? DocumentNumber { get; set; }

    /// <summary>Modelo do documento (ex.: "55").</summary>
    public string? DocumentModel { get; set; }

    /// <summary>Impressão (SHA-256) do conteúdo cru já processado — chave da idempotência por conteúdo.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Quantas vezes o status já foi consultado (para o limite do 204 eterno).</summary>
    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
