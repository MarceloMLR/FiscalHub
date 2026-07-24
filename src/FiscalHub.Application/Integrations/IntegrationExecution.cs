namespace FiscalHub.Application.Integrations;

/// <summary>Um disparo de integração de um período (manual ou agendado), para registro/auditoria.</summary>
public sealed record IntegrationExecution
{
    public required IntegrationMode Mode { get; init; }

    public required string TenantId { get; init; }

    public required string CompanyCode { get; init; }

    /// <summary>Filial alvo; <c>null</c> = todas.</summary>
    public string? BranchCode { get; init; }

    public required DateTimeOffset PeriodStart { get; init; }

    public required DateTimeOffset PeriodEnd { get; init; }

    /// <summary>Quantas notas a descoberta achou (e enfileirou) neste disparo.</summary>
    public required int DiscoveredCount { get; init; }

    /// <summary>Agendamento que originou o disparo; <c>null</c> se foi manual.</summary>
    public int? ScheduleId { get; init; }
}
