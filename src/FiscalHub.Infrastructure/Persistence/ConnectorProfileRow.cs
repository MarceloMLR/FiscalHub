namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Linha de persistência do perfil de conector de um tenant (um por tenant): a entrada, os chamados e os módulos. A saída
/// mora nas contas de plataforma e nos conectores delas (<see cref="PlatformAccountRow"/>, ADR-0036).
/// </summary>
internal sealed class ConnectorProfileRow
{
    public int Id { get; set; }

    public required string TenantId { get; set; }

    public required string InboundAdapter { get; set; }

    public required string InboundSettings { get; set; }

    /// <summary>Adapter de chamados (opcional). Nulo = tenant sem abertura de chamado configurada.</summary>
    public string? SupportAdapter { get; set; }

    public string? SupportSettings { get; set; }

}
