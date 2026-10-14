using FiscalHub.Application.Connectors;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// A declaração do adapter Avalara (change platform-account-and-connectors, 5.1; ADR-0037): o nome, a plataforma, e o que
/// ele atende — o Fiscal na base da v2, sem lote; o Contábil e o Inventário na base da v1, em lote. A declaração é código,
/// registrada no DI como a porta, e não é gravada.
/// </summary>
public class AvalaraDeclarationTests
{
    [Fact]
    public void The_adapter_registers_its_declaration_with_its_name_and_its_platform()
    {
        using ServiceProvider services = new ServiceCollection().AddAvalaraComplianceDispatcher().BuildServiceProvider();

        IAdapterDeclaration declaration = Assert.Single(services.GetServices<IAdapterDeclaration>());

        Assert.Equal("Avalara", declaration.Adapter);
        Assert.Equal("Avalara", declaration.Platform);
    }

    [Fact]
    public void It_declares_the_modules_and_integrations_of_the_adr_0037()
    {
        string[] lines = [.. new AvalaraDeclaration().Modules.SelectMany(m => m.Integrations.Select(
            i => $"{m.Module} {i.Key} {i.Version} {(i.AcceptsBatch ? "lote" : "sem lote")}"))];

        Assert.Equal(
            [
                "Fiscal notas-fiscais v2 sem lote",
                "Contabil plano-de-contas v1 lote",
                "Contabil lancamentos v1 lote",
                "Contabil saldos v1 lote",
                "Inventario k200 v1 lote",
                "Inventario k210-k215 v1 lote",
                "Inventario k230 v1 lote",
            ],
            lines);
    }

    [Fact]
    public void Its_versions_are_the_ones_its_integrations_use_and_the_dispatch_uses_the_one_of_the_invoices()
    {
        var declaration = new AvalaraDeclaration();

        Assert.Equal(["v2", "v1"], AdapterDeclarations.VersionsOf(declaration));
        DeclaredIntegration invoices = declaration.Modules.SelectMany(m => m.Integrations).Single(i => i.Key == ConnectorIntegrations.Invoices);
        Assert.Equal(AvalaraOutboundSettings.DispatchVersion, invoices.Version);
    }

    [Fact]
    public void Its_names_come_from_the_hub_vocabulary()
    {
        var declaration = new AvalaraDeclaration();

        Assert.All(declaration.Modules, m => Assert.Contains(m.Module, TenantModules.Known));
        Assert.All(declaration.Modules.SelectMany(m => m.Integrations), i => Assert.Contains(i.Key, ConnectorIntegrations.Known));
    }
}
