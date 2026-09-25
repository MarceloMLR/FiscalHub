using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>Especifica o registro no DI: feed scoped do D365, client credentials por padrão, Azure CLI só por opção.</summary>
public class D365PollRegistrationTests
{
    [Fact]
    public async Task Registers_the_d365_feed_with_client_credentials_by_default()
    {
        await using ServiceProvider sp = Build(services => services.AddD365ChangeFeed());
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        IDocumentChangeFeed feed = scope.ServiceProvider.GetRequiredService<IDocumentChangeFeed>();

        Assert.Equal("Dynamics365", feed.Origin);
        Assert.IsType<ClientCredentialsD365TokenProvider>(sp.GetRequiredService<ID365TokenProvider>());
    }

    [Fact]
    public async Task Azure_cli_token_replaces_client_credentials_only_when_asked()
    {
        await using ServiceProvider sp = Build(services => services.AddD365ChangeFeed().UseD365AzureCliToken());

        Assert.IsType<AzureCliD365TokenProvider>(sp.GetRequiredService<ID365TokenProvider>());
    }

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IConnectorProfileStore, D365ChangeFeedTests.FakeProfiles>();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
