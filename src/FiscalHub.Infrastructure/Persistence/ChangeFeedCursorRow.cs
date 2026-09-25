namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Linha de persistência do cursor do feed de mudanças de um par (tenant, origem).</summary>
internal sealed class ChangeFeedCursorRow
{
    public int Id { get; set; }

    public required string TenantId { get; set; }

    public required string Origin { get; set; }

    /// <summary>Marca d'água em ticks UTC — inteiro compara/ordena em qualquer provider (SQLite inclusive). Nulo = não iniciado.</summary>
    public long? WatermarkTicks { get; set; }

    public long? LastPolledTicks { get; set; }

    public long? NotBeforeTicks { get; set; }

    public int ConsecutiveFailures { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
