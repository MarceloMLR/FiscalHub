namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Linha de persistência do perfil de conector de um tenant (um por tenant).</summary>
internal sealed class ConnectorProfileRow
{
    public int Id { get; set; }

    public required string TenantId { get; set; }

    public required string Environment { get; set; }

    public bool Realtime { get; set; }

    public required string InboundAdapter { get; set; }

    public required string InboundSettings { get; set; }

    public required string OutboundAdapter { get; set; }

    public required string OutboundSettings { get; set; }
}
