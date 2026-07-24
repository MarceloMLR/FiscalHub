namespace FiscalHub.Application.Integrations;

/// <summary>
/// Um agendamento de integração. <c>ScheduledDaily</c> roda todo dia processando o dia anterior
/// (D-1); <c>ScheduledOnce</c> roda uma vez, numa data/hora futura, sobre um período explícito.
/// </summary>
public sealed record ScheduledIntegration
{
    public int Id { get; init; }

    /// <summary>Só <c>ScheduledDaily</c> ou <c>ScheduledOnce</c> (nunca <c>Manual</c>).</summary>
    public required IntegrationMode Mode { get; init; }

    public required string TenantId { get; init; }

    public required string CompanyCode { get; init; }

    /// <summary>Filial alvo; <c>null</c> = todas.</summary>
    public string? BranchCode { get; init; }

    /// <summary>Período explícito (yyyy-MM-dd) do agendamento único. <c>null</c> no diário (é D-1).</summary>
    public string? PeriodStart { get; init; }

    public string? PeriodEnd { get; init; }

    /// <summary>Quando roda a próxima (e única, no caso Once) vez.</summary>
    public required DateTimeOffset NextRunAt { get; init; }

    public bool Active { get; init; } = true;
}
