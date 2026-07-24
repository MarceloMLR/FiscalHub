namespace FiscalHub.Application.Integrations;

/// <summary>
/// Executa uma integração de um período: descobre as notas na origem, enfileira cada referência e
/// registra a execução. Compartilhado pela integração manual e pelo agendador — a diferença é só o
/// modo (e a política de idempotência que ele implica).
/// </summary>
public interface IIntegrationRunner
{
    /// <summary>Roda o disparo e devolve quantas notas foram descobertas/enfileiradas.</summary>
    Task<int> RunAsync(RunRequest request, CancellationToken ct = default);
}

/// <summary>Parâmetros de um disparo de integração.</summary>
public sealed record RunRequest
{
    public required IntegrationMode Mode { get; init; }
    public required string TenantId { get; init; }
    public required string CompanyCode { get; init; }

    /// <summary>Filial alvo; <c>null</c> = todas.</summary>
    public string? BranchCode { get; init; }

    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }

    /// <summary>Agendamento de origem, se houver.</summary>
    public int? ScheduleId { get; init; }
}
