using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// O perfil da Avalara na forma da change platform-account-and-connectors: a conta de plataforma, com a credencial e o
/// endpoint de token, e o conector Avalara sob ela, com o mapa de URLs base por versão, a tabela establishments e o
/// módulo Fiscal com a integração de notas — no caminho <see cref="InvoicesPath"/>, o padrão antigo do
/// <c>Avalara:DocumentsPath</c>, para os testes que não falam do caminho.
/// </summary>
internal static class AvalaraProfiles
{
    public const string TokenUrl = "https://avalara-a/oauth/token";

    public const string InvoicesPath = "documents";

    /// <summary>O módulo Fiscal com a integração de notas no caminho dado; nulo, o conector sem módulo nenhum.</summary>
    public static IReadOnlyList<ConnectorModule> Invoices(string? path) => path is null
        ? []
        : [new ConnectorModule { Module = TenantModules.Fiscal, Integrations = [new ConnectorIntegration { Key = ConnectorIntegrations.Invoices, Endpoint = path }] }];

    /// <summary>O nome do Client Secret da conta Avalara do tenant, como o servidor o deriva.</summary>
    public static string SecretOf(string tenantId) => $"fh-{tenantId}--platform--avalara--clientsecret";

    /// <summary>As settings do conector com a URL base da v2 e, se houver, mais propriedades (começando pela vírgula).</summary>
    public static string Connector(string baseUrl, string more = "") => "{\"baseUrls\":{\"v2\":\"" + baseUrl + "\"}" + more + "}";

    /// <summary>A conta Avalara do tenant, com o conector Avalara sob ela.</summary>
    public static PlatformAccount Account(
        string tenantId, string connectorSettings, string? clientId = "id-a", string? tokenUrl = TokenUrl, bool withSecret = true,
        string? invoicesPath = InvoicesPath)
        => new()
        {
            Platform = "Avalara",
            ClientId = clientId,
            ClientSecretRef = withSecret ? SecretReference.Of(SecretOf(tenantId)) : null,
            TokenUrl = tokenUrl,
            Connectors = [new PlatformConnector { Adapter = "Avalara", Settings = connectorSettings, Modules = Invoices(invoicesPath) }],
        };

    /// <summary>O perfil do tenant com a conta Avalara e o conector Avalara.</summary>
    public static TenantConnectorProfile Profile(
        string connectorSettings, string tenantId = "tenant-a", string inboundAdapter = "Dynamics365", string? clientId = "id-a",
        string? tokenUrl = TokenUrl, bool withSecret = true, string? invoicesPath = InvoicesPath)
        => new()
        {
            TenantId = tenantId,
            InboundAdapter = inboundAdapter,
            Accounts = [Account(tenantId, connectorSettings, clientId, tokenUrl, withSecret, invoicesPath)],
        };
}
