using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica os módulos do tenant (change <c>module-navigation-and-integration-panel</c>, D2): desde a change
/// <c>platform-account-and-connectors</c> (D7), a união dos módulos ligados nos conectores dele, na ordem da barra lateral,
/// e só o Fiscal quando nenhum está ligado. É apresentação, e não permissão.
/// </summary>
public class TenantModulesTests
{
    [Fact]
    public void Without_a_connector_or_a_module_on_the_tenant_has_only_fiscal()
    {
        Assert.Equal(["Fiscal"], TenantModules.Of(null));
        Assert.Equal(["Fiscal"], TenantModules.Of(Profile()));
        Assert.Equal(["Fiscal"], TenantModules.Of(Profile(Connector("Avalara"))));
    }

    [Fact]
    public void The_modules_are_the_union_of_the_connectors_in_the_sidebar_order_counted_once()
    {
        // A ordem dos conectores e a dos módulos dentro deles não decidem a ordem da lista.
        TenantConnectorProfile profile = Profile(
            Connector("Avalara", TenantModules.Inventory, TenantModules.Fiscal),
            Connector("Outro", TenantModules.Fiscal));

        Assert.Equal(["Fiscal", "Inventario"], TenantModules.Of(profile));
    }

    [Fact]
    public void A_tenant_without_fiscal_on_any_connector_does_not_get_it()
    {
        Assert.Equal(["Inventario"], TenantModules.Of(Profile(Connector("Avalara", TenantModules.Inventory))));
    }

    private static TenantConnectorProfile Profile(params PlatformConnector[] connectors) => new()
    {
        TenantId = "tenant-a",
        InboundAdapter = "Dynamics365",
        Accounts = connectors.Length == 0 ? [] : [new PlatformAccount { Platform = "Avalara", Connectors = connectors }],
    };

    private static PlatformConnector Connector(string adapter, params string[] modules) => new()
    {
        Adapter = adapter,
        Modules = [.. modules.Select(m => new ConnectorModule { Module = m })],
    };
}
