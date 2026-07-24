namespace FiscalHub.Application.Integrations;

/// <summary>Leitura das execuções para o painel (tela de Agendamentos/Execuções).</summary>
public interface IExecutionQueries
{
    Task<IReadOnlyList<ExecutionSummary>> ListRecentAsync(int max, CancellationToken ct = default);
}

/// <summary>Linha de execução exibida no painel: modo, empresa/filial, período e quantas notas.</summary>
public sealed record ExecutionSummary
{
    public required int Id { get; init; }
    public required IntegrationMode Mode { get; init; }
    public required string CompanyCode { get; init; }
    public string? BranchCode { get; init; }
    public required string PeriodStart { get; init; }   // yyyy-MM-dd
    public required string PeriodEnd { get; init; }
    public required int DiscoveredCount { get; init; }
    public required DateTimeOffset RunAt { get; init; }
}
