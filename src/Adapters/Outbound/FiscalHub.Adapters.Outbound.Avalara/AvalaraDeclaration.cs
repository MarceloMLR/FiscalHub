using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// A declaração do adapter Avalara (change <c>platform-account-and-connectors</c>, 5.1; ADR-0037): o Tax Compliance atende
/// o Fiscal, na base da <c>v2</c> e sem lote, e o Contábil e o Inventário, na base da <c>v1</c> e em lote. O Contábil e o
/// Inventário entram como forma: nenhum endpoint real deles existe ainda, e cada um é uma fatia própria.
/// </summary>
internal sealed class AvalaraDeclaration : IAdapterDeclaration
{
    /// <summary>A versão de base das integrações do Contábil e do Inventário.</summary>
    internal const string RecordsVersion = "v1";

    public string Adapter => AvalaraServiceCollectionExtensions.AdapterName;

    public string Platform => AvalaraServiceCollectionExtensions.PlatformName;

    public IReadOnlyList<DeclaredModule> Modules { get; } =
    [
        new(TenantModules.Fiscal,
        [
            new(ConnectorIntegrations.Invoices, AvalaraOutboundSettings.DispatchVersion, AcceptsBatch: false),
        ]),
        new(TenantModules.Accounting,
        [
            new(ConnectorIntegrations.ChartOfAccounts, RecordsVersion, AcceptsBatch: true),
            new(ConnectorIntegrations.JournalEntries, RecordsVersion, AcceptsBatch: true),
            new(ConnectorIntegrations.Balances, RecordsVersion, AcceptsBatch: true),
        ]),
        new(TenantModules.Inventory,
        [
            new(ConnectorIntegrations.BlockK200, RecordsVersion, AcceptsBatch: true),
            new(ConnectorIntegrations.BlockK210K215, RecordsVersion, AcceptsBatch: true),
            new(ConnectorIntegrations.BlockK230, RecordsVersion, AcceptsBatch: true),
        ]),
    ];
}
