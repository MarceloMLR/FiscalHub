using FiscalHub.Application.Integrations;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Linha de persistência de um agendamento de integração.</summary>
internal sealed class ScheduledIntegrationRow
{
    public int Id { get; set; }

    public IntegrationMode Mode { get; set; }

    public required string TenantId { get; set; }

    public required string CompanyCode { get; set; }

    public string? BranchCode { get; set; }

    public string? PeriodStart { get; set; }

    public string? PeriodEnd { get; set; }

    /// <summary>Próximo disparo em ticks UTC — inteiro compara/ordena em qualquer provider (SQLite inclusive).</summary>
    public long NextRunTicks { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
