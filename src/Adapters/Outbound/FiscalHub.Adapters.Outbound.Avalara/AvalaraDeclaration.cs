using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// A declaração do adapter Avalara (change <c>platform-account-and-connectors</c>, 5.1; ADR-0037): o Tax Compliance atende
/// o Fiscal, na base da <c>v2</c> e sem lote, e o Contábil e o Inventário, na base da <c>v1</c> e em lote. O Contábil e o
/// Inventário entram como forma: nenhum endpoint real deles existe ainda, e cada um é uma fatia própria.
/// <para>O rótulo e o agendamento vêm da change <c>connector-navigation-and-admin-json</c> (D3 e D4): este conector se
/// chama <c>Tax Compliance</c> na tela, e ele agenda — a captura no ERP é disparada por horário.</para>
/// </summary>
internal sealed class AvalaraDeclaration : IAdapterDeclaration
{
    /// <summary>A versão de base das integrações do Contábil e do Inventário.</summary>
    internal const string RecordsVersion = "v1";

    /// <summary>O nome do produto na tela. "Avalara" é a plataforma; o conector é o Tax Compliance (ADR-0039 §1).</summary>
    internal const string ConnectorLabel = "Tax Compliance";

    public string Adapter => AvalaraServiceCollectionExtensions.AdapterName;

    public string Platform => AvalaraServiceCollectionExtensions.PlatformName;

    public string Label => ConnectorLabel;

    /// <summary>Agenda: a captura das notas no ERP é disparada por horário (ADR-0039 §2).</summary>
    public bool Scheduling => true;

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
