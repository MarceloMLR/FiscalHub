using System.Text.Json;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o contrato do <c>/info</c> (change <c>platform-account-and-connectors</c>, D6): o ambiente é o da instância,
/// e nunca o do perfil nem um padrão; os outros campos não mudam de forma.
/// </summary>
public class InstanceInfoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Sandbox")]
    [InlineData("Production")]
    public void The_environment_is_the_instance_one_whatever_the_profile(string environment)
    {
        InstanceInfo info = InstanceInfo.For(InstanceEnvironment.From(environment), Profile(), []);

        Assert.Equal(environment, info.Environment);
    }

    [Fact]
    public void Without_a_profile_the_environment_is_still_the_instance_one()
    {
        InstanceInfo info = InstanceInfo.For(InstanceEnvironment.From("Production"), null, []);

        Assert.Equal("Production", info.Environment);
        Assert.False(info.AutomaticIntegration);
        Assert.False(info.InboundScans);
        Assert.Equal(["Fiscal"], info.Modules);
    }

    [Fact]
    public void A_tenant_with_a_profile_and_no_connector_gets_only_fiscal()
    {
        // Os módulos vêm dos conectores (D7), e um tenant sem conector não tem nenhum ligado: a resposta é o padrão, só o
        // Fiscal, como antes desta change — e não a lista vazia, que deixaria a barra lateral só com o Agendamento.
        InstanceInfo info = InstanceInfo.For(InstanceEnvironment.From("Sandbox"), Profile(), []);

        Assert.Equal(["Fiscal"], info.Modules);
    }

    [Fact]
    public void The_response_keeps_the_same_four_fields()
    {
        InstanceInfo info = InstanceInfo.For(
            InstanceEnvironment.From("Sandbox"), Profile(modules: ["Fiscal", "Inventario"]), ["Dynamics365"]);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(info, Web));
        Assert.Equal(["environment", "automaticIntegration", "inboundScans", "modules"],
            json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Sandbox", json.RootElement.GetProperty("environment").GetString());
        Assert.True(json.RootElement.GetProperty("inboundScans").GetBoolean());
        Assert.False(json.RootElement.GetProperty("automaticIntegration").GetBoolean());   // poll.enabled não está ligado
        Assert.Equal(["Fiscal", "Inventario"], json.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
    }

    // O perfil com um conector Avalara ligando os módulos dados: os módulos do /info vêm dos conectores (D7).
    private static TenantConnectorProfile Profile(IReadOnlyList<string>? modules = null) => new()
    {
        TenantId = "tenant-a",
        InboundAdapter = "Dynamics365",
        Accounts = modules is null
            ? []
            : [new PlatformAccount
            {
                Platform = "Avalara",
                Connectors = [new PlatformConnector { Adapter = "Avalara", Modules = [.. modules.Select(m => new ConnectorModule { Module = m })] }],
            }],
    };
}
