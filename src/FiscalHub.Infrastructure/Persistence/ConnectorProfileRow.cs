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

    /// <summary>
    /// Se o hub envia ao conector de saída, ou apenas salva (ADR-0041). Nasce ligada, como o comportamento de sempre; a
    /// migração cobre as linhas existentes com o <c>DEFAULT 1</c>. O padrão fica no CLR, e não no modelo EF, como no
    /// <see cref="UserRow.Active"/>: um <c>HasDefaultValue(true)</c> faria o EF omitir a coluna no INSERT justamente
    /// quando o valor é <c>false</c> (o sentinela de um <c>bool</c> é o default do CLR), e um perfil criado já com a
    /// chave desligada seria gravado ligado.
    /// </summary>
    public bool OutboundDispatchEnabled { get; set; } = true;
}
