using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica o registro no DI: feed scoped do D365 e o client credentials como a única identidade do conector, sem
/// provider nem método de registro que troque a identidade por ambiente.
/// </summary>
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

    // ---------- a credencial do perfil é a única identidade (change explicit-credential-and-execution-cnpj, D4) ----------

    [Fact]
    public async Task Every_d365_registration_resolves_the_one_client_credentials_provider()
    {
        // Os quatro registros do adapter, como o host os faz.
        await using ServiceProvider sp = Build(services =>
        {
            services.AddD365ChangeFeed();
            services.AddD365GoodsInvoiceSource();
            services.AddD365CompanyDirectory();
            services.AddD365DocumentDiscovery();
        });

        ID365TokenProvider provider = Assert.Single(sp.GetServices<ID365TokenProvider>());
        Assert.IsType<ClientCredentialsD365TokenProvider>(provider);
    }

    [Fact]
    public void The_only_token_provider_in_the_adapter_is_client_credentials()
    {
        // O adapter não lê o ambiente do host: sem outra implementação, nenhum ramo de Development tem o que escolher.
        Type[] providers =
        [
            .. typeof(ID365TokenProvider).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ID365TokenProvider).IsAssignableFrom(t)),
        ];

        Assert.Equal([typeof(ClientCredentialsD365TokenProvider)], providers);
    }

    [Fact]
    public void The_registration_surface_is_exactly_the_four_add_methods()
    {
        // Nenhum Use* que um host chamaria sob IsDevelopment() para trocar a identidade do conector.
        string[] methods =
        [
            .. typeof(D365PollServiceCollectionExtensions)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Select(m => m.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["AddD365ChangeFeed", "AddD365CompanyDirectory", "AddD365DocumentDiscovery", "AddD365GoodsInvoiceSource"],
            methods);
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
    public async Task Company_directory_of_the_d365_resolves_with_the_collector_token()
    {
        await using ServiceProvider sp = Build(services =>
        {
            services.AddD365ChangeFeed();
            services.AddD365CompanyDirectory();
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        ICompanyDirectory directory = Assert.Single(scope.ServiceProvider.GetServices<ICompanyDirectory>());

        Assert.Equal("Dynamics365", directory.Origin);   // casa com o adapter de entrada do perfil do tenant-a
        Assert.IsType<D365CompanyDirectory>(directory);
        Assert.IsType<ClientCredentialsD365TokenProvider>(sp.GetRequiredService<ID365TokenProvider>());
    }

    [Fact]
    public async Task Period_discovery_of_the_d365_resolves_with_the_collector_token()
    {
        await using ServiceProvider sp = Build(services =>
        {
            services.AddD365ChangeFeed();
            services.AddD365DocumentDiscovery();
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        IDocumentDiscovery discovery = Assert.Single(scope.ServiceProvider.GetServices<IDocumentDiscovery>());

        Assert.Equal("Dynamics365", discovery.Origin);
        Assert.IsType<D365DocumentDiscovery>(discovery);
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
