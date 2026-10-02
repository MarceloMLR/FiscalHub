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

    /// <summary>
    /// Lista os grupos (empresa/filial/dia da execução/período/tipo/modelo/modo) com as contagens por status. Com a janela,
    /// só os grupos cujo dia da execução está nela, e do modelo dela: o mesmo filtro dos cards (D15).
    /// </summary>
    Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, GroupWindow? window = null, CancellationToken ct = default);

    /// <summary>
    /// Lista os documentos de um grupo: a linha inteira da tabela, para o modal contar o mesmo que o título (change
    /// erp-company-directory-and-card-filters, D10 e D15). O modo <c>Automatic</c> casa também o modo nulo, como os grupos o
    /// servem.
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(GroupKey group, CancellationToken ct = default);

    /// <summary>
    /// As contagens dos cards por modelo, sobre todas as notas com grupo cujo dia da execução está entre
    /// <paramref name="from"/> e <paramref name="to"/>, inclusive (D9 e D15). Uma linha por modelo.
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

    /// <summary>Quantas vezes o reprocesso foi aceito para a nota; o modal o mostra ao lado das consultas.</summary>
    public int Reprocessings { get; init; }
}

/// <summary>
/// Grupo de documentos (empresa/filial/dia da execução/período/tipo/modelo/modo) com contagens por estado — linha do
/// dashboard. O dia é o da execução que trouxe as notas por último, e não a data fiscal delas (D15).
/// </summary>
public sealed record DocumentGroup
{
    public required string CompanyCode { get; init; }
    public required string BranchCode { get; init; }

    /// <summary>O dia da execução (aaaa-mm-dd, em Brasília): o da integração, ou o da busca do coletor.</summary>
    public required string ExecutedOn { get; init; }

    /// <summary>O período integrado (aaaa-mm-dd); nulo na automática.</summary>
    public string? PeriodStart { get; init; }

    public string? PeriodEnd { get; init; }

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

/// <summary>A janela de dias da execução e o modelo, inclusive nas duas pontas: o filtro dos cards e da tabela.</summary>
public sealed record GroupWindow(DateOnly From, DateOnly To, string? Model);

/// <summary>
/// A linha da tabela que o modal abre. <see cref="MatchPeriod"/> falso = sem filtro de período (a consulta de antes); verdadeiro
/// com o período nulo = a linha da automática, sem período.
/// </summary>
public sealed record GroupKey
{
    public required string CompanyCode { get; init; }
    public required string BranchCode { get; init; }
    public required string ExecutedOn { get; init; }
    public DocumentType? Type { get; init; }
    public string? Model { get; init; }
    public string? Trigger { get; init; }
    public bool MatchPeriod { get; init; }
    public string? PeriodStart { get; init; }
    public string? PeriodEnd { get; init; }
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
