using FiscalHub.Application.Integrations;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Linha de persistência de uma execução de integração (manual/agendada).</summary>
internal sealed class IntegrationExecutionRow
{
    public int Id { get; set; }

    public IntegrationMode Mode { get; set; }

    public required string TenantId { get; set; }

    public required string CompanyCode { get; set; }

    public string? BranchCode { get; set; }

    /// <summary>Início do período (yyyy-MM-dd) — string ordena nos dois providers.</summary>
    public required string PeriodStart { get; set; }

    public required string PeriodEnd { get; set; }

    public int DiscoveredCount { get; set; }

    public int? ScheduleId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
