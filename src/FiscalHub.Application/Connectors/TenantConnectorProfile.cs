namespace FiscalHub.Application.Connectors;

/// <summary>
/// Configuração do conector de um tenant: qual adapter de entrada (ERP) e de saída (compliance),
/// o ambiente ativo e as settings específicas de cada adapter (JSON — cada adapter tem seu próprio
/// schema). Se a integração é automática não é campo daqui: é o <c>poll.enabled</c> das settings de
/// entrada (ADR-0029). Segredos NÃO ficam aqui em claro: as settings guardam
/// referências (nome no Key Vault), nunca o valor cru.
/// </summary>
public sealed record TenantConnectorProfile
{
    public required string TenantId { get; init; }

    /// <summary>Ambiente ativo: "Sandbox" ou "Production".</summary>
    public required string Environment { get; init; }

    /// <summary>Adapter de entrada (ERP): ex. "Xml", "Dynamics365", "iScala".</summary>
    public required string InboundAdapter { get; init; }

    /// <summary>Settings do adapter de entrada (JSON; schema é do adapter). Segredos por referência.</summary>
    public string InboundSettings { get; init; } = "{}";

    /// <summary>Adapter de saída (compliance): ex. "Avalara", "ThomsonReuters".</summary>
    public required string OutboundAdapter { get; init; }

    /// <summary>Settings do adapter de saída (JSON; por ambiente). Segredos por referência.</summary>
    public string OutboundSettings { get; init; } = "{}";

    /// <summary>Adapter de chamados de suporte (opcional): ex. "Freshdesk", "Local". Nulo = sem suporte.</summary>
    public string? SupportAdapter { get; init; }

    /// <summary>Settings do adapter de chamados (JSON; domínio + credenciais por referência).</summary>
    public string SupportSettings { get; init; } = "{}";

    /// <summary>
    /// Os módulos que o tenant tem, na ordem da barra lateral (<see cref="TenantModules"/>). Nulo = nenhum gravado, e vale
    /// o padrão (só o Fiscal). Apresentação, e não permissão.
    /// </summary>
    public IReadOnlyList<string>? Modules { get; init; }
}
