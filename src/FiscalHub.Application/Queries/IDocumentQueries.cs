using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Queries;

/// <summary>
/// Lado de leitura do rastreio: consultas para telas (dashboard). Separado do <c>IProcessingStore</c>
/// (comandos) para não misturar leitura e escrita — e para não engordar a porta de escrita nem os
/// seus fakes de teste.
/// </summary>
public interface IDocumentQueries
{
    /// <summary>Lista os documentos mais recentes, do mais novo para o mais antigo.</summary>
    Task<IReadOnlyList<DocumentSummary>> ListRecentAsync(int limit, CancellationToken ct = default);

    /// <summary>Lista os grupos (empresa/filial/dia/tipo) com as contagens por status.</summary>
    Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, CancellationToken ct = default);

    /// <summary>Lista os documentos de um grupo específico (empresa/filial/dia).</summary>
    Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(
        string companyCode, string branchCode, string referenceDate, CancellationToken ct = default);
}

/// <summary>Visão de leitura de um documento para o dashboard.</summary>
public sealed record DocumentSummary
{
    public required string TenantId { get; init; }
    public required string NaturalKey { get; init; }
    public required DocumentType Type { get; init; }
    public required IntegrationStatus Status { get; init; }
    public required int Attempts { get; init; }
    public string? ExternalId { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Grupo de documentos (empresa/filial/dia/tipo) com contagens por estado — linha do dashboard.</summary>
public sealed record DocumentGroup
{
    public required string CompanyCode { get; init; }
    public required string BranchCode { get; init; }
    public required string ReferenceDate { get; init; }
    public required DocumentType Type { get; init; }
    public required int Total { get; init; }
    public required int Finalizadas { get; init; }
    public required int EmProcessamento { get; init; }
    public required int ComErro { get; init; }
}
