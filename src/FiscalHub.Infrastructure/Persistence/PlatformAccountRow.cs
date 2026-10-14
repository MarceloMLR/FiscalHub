namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Linha de persistência da conta de plataforma de um tenant (ADR-0036): uma por plataforma, com a credencial tipada — o
/// Client ID, a referência do segredo e o endpoint de token — e os conectores dela.
/// </summary>
internal sealed class PlatformAccountRow
{
    public int Id { get; set; }

    public required string TenantId { get; set; }

    public required string Platform { get; set; }

    public string? ClientId { get; set; }

    /// <summary>A referência do segredo (<c>kv:&lt;nome&gt;</c>). Nunca o valor.</summary>
    public string? ClientSecretRef { get; set; }

    public string? TokenUrl { get; set; }

    public List<ConnectorRow> Connectors { get; set; } = [];
}

/// <summary>Linha de persistência de um conector da conta: o adapter, que é a chave dentro da conta, e as settings do produto.</summary>
internal sealed class ConnectorRow
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public required string Adapter { get; set; }

    /// <summary>As settings do produto (JSON): o mapa de URLs base por versão e o resto. Segredos por referência.</summary>
    public required string Settings { get; set; }

    public List<ConnectorModuleRow> Modules { get; set; } = [];
}

/// <summary>Linha de persistência de um módulo ligado num conector (D3): o nome dele, único dentro do conector.</summary>
internal sealed class ConnectorModuleRow
{
    public int Id { get; set; }

    public int ConnectorId { get; set; }

    public required string Module { get; set; }

    public List<ConnectorIntegrationRow> Integrations { get; set; } = [];
}

/// <summary>
/// Linha de persistência de uma integração ligada num módulo (D3): a chave, o caminho e o lote. Repete o tenant, porque a
/// regra de um conector por integração guarda-se num índice em (tenant, chave), e um índice não atravessa joins.
/// </summary>
internal sealed class ConnectorIntegrationRow
{
    public int Id { get; set; }

    public int ModuleId { get; set; }

    public required string TenantId { get; set; }

    public required string Key { get; set; }

    /// <summary>O caminho, relativo à base da versão. Vazio: inativa (D5). Nulo: à espera do host (D10).</summary>
    public string? Endpoint { get; set; }

    public int? BatchSize { get; set; }
}
