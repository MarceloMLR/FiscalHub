using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica os módulos do tenant (change <c>module-navigation-and-integration-panel</c>, D2): o padrão sem módulos
/// gravados é só o Fiscal, a lista é normalizada na ordem da barra lateral, e o que não é aceito é recusado com o valor
/// nomeado. É apresentação, e não permissão.
/// </summary>
public class TenantModulesTests
{
    [Fact]
    public void Without_stored_modules_the_tenant_has_only_fiscal()
    {
        Assert.Equal(["Fiscal"], TenantModules.Of(null));
        Assert.Equal(["Fiscal"], TenantModules.Of(Profile(modules: null)));
    }

    [Fact]
    public void Stored_modules_are_the_tenant_modules()
    {
        Assert.Equal(["Fiscal", "Inventario"], TenantModules.Of(Profile(modules: ["Fiscal", "Inventario"])));
    }

    [Theory]
    [InlineData(new[] { "Inventario", "Fiscal" }, new[] { "Fiscal", "Inventario" })]
    [InlineData(new[] { "Contabil", "Contabil", "Fiscal" }, new[] { "Fiscal", "Contabil" })]
    [InlineData(new[] { "inventario", "FISCAL" }, new[] { "Fiscal", "Inventario" })]
    [InlineData(new[] { "Inventario" }, new[] { "Inventario" })]
    public void Valid_list_is_normalized_in_the_sidebar_order(string[] requested, string[] expected)
    {
        Assert.True(TenantModules.TryNormalize(requested, out IReadOnlyList<string> modules, out string? problem));
        Assert.Equal(expected, modules);
        Assert.Null(problem);
    }

    [Fact]
    public void Unknown_value_is_refused_naming_it_and_the_accepted_ones()
    {
        Assert.False(TenantModules.TryNormalize(["Fiscal", "Folha"], out _, out string? problem));
        Assert.Contains("'Folha'", problem);
        Assert.Contains("Fiscal, Contabil e Inventario", problem);
    }

    [Fact]
    public void Empty_list_is_refused()
    {
        Assert.False(TenantModules.TryNormalize([], out _, out string? problem));
        Assert.Contains("pelo menos um módulo", problem);
    }

    private static TenantConnectorProfile Profile(IReadOnlyList<string>? modules) => new()
    {
        TenantId = "tenant-a",
        Environment = "Sandbox",
        InboundAdapter = "Dynamics365",
        OutboundAdapter = "Avalara",
        Modules = modules,
    };
}
