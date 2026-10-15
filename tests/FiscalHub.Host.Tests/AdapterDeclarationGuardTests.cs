using FiscalHub.Application.Connectors;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Host.Tests;

/// <summary>
/// A chave do conector é o <b>nome do adapter</b>, e ela tem que ser única entre as declarações registradas (tarefa 4.0
/// da change <c>connector-navigation-and-admin-json</c>). Não é requisito novo: o <c>IAdapterDeclaration</c> já diz "O
/// nome do adapter: a chave do conector" e "A identidade do conector continua sendo o <c>Adapter</c>, comparado exato",
/// e o design D3 e o D10 repetem. O que faltava era quem garantisse.
/// <para>A unicidade só era conferida <b>dentro de uma conta</b> (o <c>ConnectorProfileService</c> recusa o adapter
/// repetido por conta). Duas declarações de plataformas diferentes com o mesmo nome de adapter passavam: o <c>/info</c>
/// devolveria duas entradas com o mesmo <c>adapter</c>, e a barra lateral ficaria com duas chaves iguais — dois grupos
/// indistinguíveis, e o item (conector, módulo) de um abrindo a tela do outro, em silêncio.</para>
/// </summary>
public class AdapterDeclarationGuardTests
{
    [Fact]
    public void The_same_adapter_declared_by_two_platforms_brings_the_host_down()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Guard(
            new FakeAdapterDeclaration("Avalara", "Avalara"),
            new FakeAdapterDeclaration("Outra", "Avalara")));

        // A mensagem nomeia o adapter repetido e as duas plataformas: quem sobe o host precisa saber qual pacote tirar.
        Assert.Contains("Avalara", error.Message, StringComparison.Ordinal);
        Assert.Contains("Outra", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_adapters_of_the_same_platform_are_the_growth_path_and_pass()
    {
        // O caminho escrito na própria interface: "com o AvaTax, a plataforma é Avalara e os adapters são dois".
        Guard(
            new FakeAdapterDeclaration("Avalara", "Avalara"),
            new FakeAdapterDeclaration("Avalara", "AvalaraAvaTax"));
    }

    [Fact]
    public void The_comparison_is_exact()
    {
        // Comparado exato, como o resto (D17 da change platform-account-and-connectors): 'avalara' não é 'Avalara'.
        Guard(
            new FakeAdapterDeclaration("Avalara", "Avalara"),
            new FakeAdapterDeclaration("Outra", "avalara"));
    }

    [Fact]
    public void One_declaration_and_none_pass()
    {
        Guard(new FakeAdapterDeclaration("Avalara", "Avalara"));
        Guard();
    }

    // Pelo mesmo caminho do host: as declarações entram no contêiner uma a uma, e a guarda pergunta ao contêiner.
    private static void Guard(params IAdapterDeclaration[] declarations)
    {
        var services = new ServiceCollection();
        foreach (IAdapterDeclaration declaration in declarations)
        {
            services.AddSingleton(declaration);
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.EnsureDistinctAdapterDeclarations();
    }
}
