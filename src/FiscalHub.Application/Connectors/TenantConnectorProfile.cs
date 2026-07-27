namespace FiscalHub.Application.Connectors;

/// <summary>
/// Configuração do conector de um tenant: qual adapter de entrada (ERP) e de saída (compliance),
/// o ambiente ativo, se tem tempo real, e as settings específicas de cada adapter (JSON — cada
/// adapter tem seu próprio schema). Segredos NÃO ficam aqui em claro: as settings guardam
/// referências (nome no Key Vault), nunca o valor cru.
/// </summary>
public sealed record TenantConnectorProfile
{
    public required string TenantId { get; init; }

    /// <summary>Ambiente ativo: "Sandbox" ou "Production".</summary>
    public required string Environment { get; init; }

    /// <summary>Se o tenant integra por evento (tempo real). Alguns ERPs/clientes não conseguem.</summary>
    public required bool Realtime { get; init; }

    /// <summary>Adapter de entrada (ERP): ex. "Xml", "Dynamics365", "iScala".</summary>
    public required string InboundAdapter { get; init; }

    /// <summary>Settings do adapter de entrada (JSON; schema é do adapter). Segredos por referência.</summary>
    public string InboundSettings { get; init; } = "{}";

    /// <summary>Adapter de saída (compliance): ex. "Avalara", "ThomsonReuters".</summary>
    public required string OutboundAdapter { get; init; }

    /// <summary>Settings do adapter de saída (JSON; por ambiente). Segredos por referência.</summary>
    public string OutboundSettings { get; init; } = "{}";
}
