namespace FiscalHub.Application.Connectors;

/// <summary>
/// A conta de plataforma do tenant (ADR-0036 §2, change <c>platform-account-and-connectors</c>, D1): a credencial da
/// plataforma de compliance, uma por plataforma, e os conectores que ela atende. A credencial é uma só para todos os
/// conectores da conta — o token emitido vale para todos os produtos da plataforma naquele ambiente —, e por isso mora
/// aqui, em campos tipados, e nunca nas settings de um conector. O endpoint de token também é da conta, e é obrigatório
/// para enviar e para testar: não existe derivação a partir da URL base de um conector (D11).
/// </summary>
public sealed record PlatformAccount
{
    /// <summary>A plataforma (ex.: "Avalara"): a identidade da conta, que não se repete no tenant.</summary>
    public required string Platform { get; init; }

    public string? ClientId { get; init; }

    /// <summary>A referência do segredo (<c>kv:&lt;nome&gt;</c>), sempre derivada pelo servidor. Nunca o valor.</summary>
    public string? ClientSecretRef { get; init; }

    /// <summary>O endpoint de token da conta. Sem ele, o envio e o teste são recusados, sem derivação.</summary>
    public string? TokenUrl { get; init; }

    public IReadOnlyList<PlatformConnector> Connectors { get; init; } = [];

    public override string ToString() => $"PlatformAccount {{ Platform = {Platform} }}";
}

/// <summary>
/// Um conector da conta, identificado pelo adapter, que é único dentro dela (D2). As settings são o blob do produto, com o
/// schema do adapter: o mapa de URLs base por versão (<c>baseUrls</c>, D4) e o resto (<c>establishments</c>…). Segredos
/// por referência. A credencial da plataforma nunca vem aqui.
/// </summary>
public sealed record PlatformConnector
{
    public required string Adapter { get; init; }

    public string Settings { get; init; } = "{}";

    /// <summary>
    /// Os módulos ligados no conector, cada um com as integrações ligadas dele (ADR-0037, D3). O adapter declara o que
    /// atende; o perfil liga o que o cliente contratou. Um módulo ligado sem integração é aceito.
    /// </summary>
    public IReadOnlyList<ConnectorModule> Modules { get; init; } = [];
}

/// <summary>Um módulo ligado num conector (um nome de <see cref="TenantModules"/>), com as integrações ligadas dele.</summary>
public sealed record ConnectorModule
{
    public required string Module { get; init; }

    public IReadOnlyList<ConnectorIntegration> Integrations { get; init; } = [];
}

/// <summary>
/// Uma integração ligada: a chave do vocabulário do hub (<see cref="ConnectorIntegrations"/>), o caminho do endpoint,
/// relativo à base da versão que ela usa, e o tamanho do lote, onde a API aceita.
/// </summary>
public sealed record ConnectorIntegration
{
    public required string Key { get; init; }

    /// <summary>
    /// O caminho, relativo à base da versão. Vazio: inativa (D5). Nulo: à espera do host, que o preenche uma vez (D10) — só
    /// entre a migração e a primeira subida; a tela nunca grava nulo.
    /// </summary>
    public string? Endpoint { get; init; }

    public int? BatchSize { get; init; }
}
