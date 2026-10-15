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
    public void It_declares_the_connector_label_and_that_it_schedules()
    {
        // O rótulo e o agendamento são características da plataforma, declaradas em código (change
        // connector-navigation-and-admin-json, D3 e D4; ADR-0037). "Avalara" é a plataforma; "Tax Compliance" é o
        // produto, e é ele que a barra lateral mostra. O agendamento: a captura no ERP é disparada por horário, e o
        // AvaTax, quando existir, declarará false (ADR-0039 §2).
        var declaration = new AvalaraDeclaration();

        Assert.Equal("Tax Compliance", declaration.Label);
        Assert.True(declaration.Scheduling);
    }

    [Fact]
    public void The_label_is_screen_text_and_never_the_identity_of_the_connector()
    {
        // A identidade continua sendo o adapter, comparado exato: o rótulo não entra em chave nem em comparação.
        IAdapterDeclaration declaration = new AvalaraDeclaration();

        Assert.NotEqual(declaration.Label, declaration.Adapter);
        Assert.Equal("Avalara", declaration.Adapter);
        Assert.Equal("Avalara", declaration.Platform);
    }

    [Fact]
    public void The_label_does_not_enter_the_secret_name()
    {
        // O nome do segredo do conector é derivado do adapter, e o rótulo não aparece nele. Mais: ele nem serviria de
        // segmento — tem espaço e maiúsculas —, e é o próprio SecretNames que recusa. É a prova de que ele é texto de
        // tela, e nada além disso.
        var declaration = new AvalaraDeclaration();

        string name = SecretNames.For("tenant-a", ConnectorSettingsKind.Connector, [declaration.Adapter], "apiKey");

        Assert.Equal("fh-tenant-a--connector--avalara--apikey", name);
        Assert.DoesNotContain("compliance", name, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(
            () => SecretNames.For("tenant-a", ConnectorSettingsKind.Connector, [declaration.Label], "apiKey"));
    }

    [Fact]
    public void Its_names_come_from_the_hub_vocabulary()
    {
        var declaration = new AvalaraDeclaration();

        Assert.All(declaration.Modules, m => Assert.Contains(m.Module, TenantModules.Known));
        Assert.All(declaration.Modules.SelectMany(m => m.Integrations), i => Assert.Contains(i.Key, ConnectorIntegrations.Known));
    }
}
