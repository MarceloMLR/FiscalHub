namespace FiscalHub.Application.Connectors;

/// <summary>
/// Configuração do conector de um tenant: o adapter de entrada (ERP) e as settings dele, as contas de plataforma de
/// compliance com os conectores de cada uma (ADR-0036), e os chamados. Settings são JSON, cada adapter com o seu schema.
/// O ambiente não é campo daqui: é da instância (<see cref="IInstanceEnvironment"/>, ADR-0036). Se a integração é
/// automática também não: é o <c>poll.enabled</c> das settings de entrada (ADR-0029). Segredos NÃO ficam aqui em claro: as
/// settings guardam referências (nome no Key Vault), nunca o valor cru.
/// </summary>
public sealed record TenantConnectorProfile
{
    public required string TenantId { get; init; }

    /// <summary>Adapter de entrada (ERP): ex. "Xml", "Dynamics365", "iScala".</summary>
    public required string InboundAdapter { get; init; }

    /// <summary>Settings do adapter de entrada (JSON; schema é do adapter). Segredos por referência.</summary>
    public string InboundSettings { get; init; } = "{}";

    /// <summary>As contas de plataforma do tenant, cada uma com a credencial e os conectores dela (ADR-0036).</summary>
    public IReadOnlyList<PlatformAccount> Accounts { get; init; } = [];

    /// <summary>Adapter de chamados de suporte (opcional): ex. "Freshdesk", "Local". Nulo = sem suporte.</summary>
    public string? SupportAdapter { get; init; }

    /// <summary>Settings do adapter de chamados (JSON; domínio + credenciais por referência).</summary>
    public string SupportSettings { get; init; } = "{}";
}
