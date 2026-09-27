using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Goods;
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

    [Fact]
    public async Task Goods_invoice_source_resolves_alongside_the_other_sources()
    {
        await using ServiceProvider sp = Build(services =>
        {
            services.AddSingleton<IInboundSource<GoodsInvoice>, XmlLikeSource>();   // o XML segue registrado
            services.AddD365ChangeFeed();
            services.AddD365GoodsInvoiceSource();
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        IInboundSource<GoodsInvoice>[] sources = [.. scope.ServiceProvider.GetServices<IInboundSource<GoodsInvoice>>()];

        Assert.Equal(["Xml", "Dynamics365"], sources.Select(s => s.Origin));
        Assert.IsType<D365GoodsInvoiceSource>(sources[1]);
        Assert.Same(sp.GetRequiredService<D365ReferenceDataCache>(), sp.GetRequiredService<D365ReferenceDataCache>());   // cache singleton
    }

    [Fact]
    public void Settle_margin_below_one_second_is_a_configuration_error()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddD365ChangeFeed(o => o.StampSettleMargin = TimeSpan.Zero));

        Assert.Contains("StampSettleMargin", ex.Message);
    }

    private sealed class XmlLikeSource : IInboundSource<GoodsInvoice>
    {
        public string Origin => "Xml";

        public string? CheckLocator(DocumentReference reference) => null;

        public Task<FetchResult<GoodsInvoice>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class EmptySecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task SetAsync(string name, string value, CancellationToken ct = default) => Task.CompletedTask;

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => Task.FromResult<SecretDescription?>(null);
    }

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IConnectorProfileStore, D365ChangeFeedTests.FakeProfiles>();
        services.AddSingleton<ISecretStore, EmptySecretStore>();   // o client credentials resolve o segredo no cofre (ADR-0027)
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
