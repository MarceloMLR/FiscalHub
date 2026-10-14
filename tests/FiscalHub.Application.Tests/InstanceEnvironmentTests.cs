using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o ambiente da instância (change <c>platform-account-and-connectors</c>, D6): vem de
/// <c>Connector:Environment</c>, e o host não sobe sem ele, nem com um valor fora de <c>Sandbox</c> e <c>Production</c>.
/// Não é o ambiente do ASP.NET, e nada deriva um eixo do outro.
/// </summary>
public class InstanceEnvironmentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_the_value_the_host_does_not_start_and_the_message_names_the_key(string? configured)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => InstanceEnvironment.From(configured));

        Assert.Contains("Connector:Environment", ex.Message);
    }

    [Fact]
    public void A_value_that_is_not_an_environment_is_refused_naming_the_key_and_the_two_accepted()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => InstanceEnvironment.From("Homologacao"));

        Assert.Contains("Connector:Environment", ex.Message);
        Assert.Contains("'Homologacao'", ex.Message);
        Assert.Contains("Sandbox", ex.Message);
        Assert.Contains("Production", ex.Message);
    }

    [Theory]
    [InlineData("Sandbox", "Sandbox")]
    [InlineData("Production", "Production")]
    [InlineData("sandbox", "Sandbox")]
    [InlineData(" PRODUCTION ", "Production")]
    public void The_two_environments_are_accepted_in_the_canonical_spelling(string configured, string expected)
    {
        Assert.Equal(expected, InstanceEnvironment.From(configured).Name);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void The_aspnet_axis_is_not_an_environment_of_the_platform(string aspNetEnvironment)
    {
        // Um host em Development aponta para o Sandbox da plataforma: o nome do ambiente do ASP.NET não serve aqui, e
        // ninguém o traduz para um ambiente da plataforma.
        Assert.Throws<InvalidOperationException>(() => InstanceEnvironment.From(aspNetEnvironment));
    }

    [Fact]
    public void A_development_host_configured_for_the_sandbox_answers_sandbox()
    {
        // A única entrada é o valor configurado: a porta não recebe o IHostEnvironment, e por construção não o lê.
        IInstanceEnvironment instance = InstanceEnvironment.From("Sandbox");

        Assert.Equal("Sandbox", instance.Name);
    }
}
