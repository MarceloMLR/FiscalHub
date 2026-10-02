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

    /// <summary>Lista os grupos (empresa/filial/dia/tipo/modelo/modo) com as contagens por status.</summary>
    Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Lista os documentos de um grupo: empresa, filial e dia e, quando informados, tipo, modelo e modo — a linha inteira da
    /// tabela, para o modal contar o mesmo que o título (change erp-company-directory-and-card-filters, D10). O modo
    /// <c>Automatic</c> casa também o modo nulo, como os grupos o servem.
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(
        string companyCode, string branchCode, string referenceDate, DocumentType? type, string? model, string? trigger,
        CancellationToken ct = default);

    /// <summary>
    /// As contagens dos cards por modelo, sobre todas as notas com grupo cuja data de referência está entre
    /// <paramref name="from"/> e <paramref name="to"/>, inclusive (D9). Uma linha por modelo.
    /// </summary>
    Task<IReadOnlyList<ModelTotals>> CountByModelAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Busca os documentos de um tenant por chave natural (seleção para abrir chamado).</summary>
    Task<IReadOnlyList<DocumentSummary>> ListByKeysAsync(
        string tenantId, IReadOnlyList<string> naturalKeys, CancellationToken ct = default);
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
    public string? Number { get; init; }
    public string? Model { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Grupo de documentos (empresa/filial/dia/tipo/modelo/modo) com contagens por estado — linha do dashboard.</summary>
public sealed record DocumentGroup
{
    public required string CompanyCode { get; init; }
    public required string BranchCode { get; init; }
    public required string ReferenceDate { get; init; }
    public required DocumentType Type { get; init; }

    /// <summary>O modelo do documento (ex.: "55", "SE"), como a origem o traz. Nulo só em registro antigo.</summary>
    public string? Model { get; init; }

    /// <summary>Modo/gatilho da integração do grupo (Automatic · Manual · ScheduledDaily · ScheduledOnce).</summary>
    public required string Trigger { get; init; }

    public required int Total { get; init; }
    public required int Finalizadas { get; init; }
    public required int EmProcessamento { get; init; }
    public required int ComErro { get; init; }
}

/// <summary>As contagens dos cards de um modelo num período — as mesmas faixas de status do <see cref="DocumentGroup"/>.</summary>
public sealed record ModelTotals
{
    /// <summary>O modelo; nulo só em registro antigo, que conta em "todos os modelos".</summary>
    public string? Model { get; init; }

    public required int Total { get; init; }
    public required int Finalizadas { get; init; }
    public required int EmProcessamento { get; init; }
    public required int ComErro { get; init; }
}
