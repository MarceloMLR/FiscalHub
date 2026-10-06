using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>Registro no DI do adapter do D365 (feed de mudanças e montagem do documento). Único ponto público — os tipos do adapter ficam internal.</summary>
public static class D365PollServiceCollectionExtensions
{
    private const string HttpClientName = "d365-odata";

    /// <summary>
    /// Registra o <c>IDocumentChangeFeed</c> do D365 (scoped: lê o perfil do tenant pelo
    /// <c>IConnectorProfileStore</c>) com token por client credentials. Requer o <c>ISecretStore</c> (resolve
    /// o <c>kv:</c> no cofre de conectores, ADR-0027) e um <c>IConnectorProfileStore</c> registrados. O client credentials é
    /// a única identidade do conector, em qualquer ambiente do host (change explicit-credential-and-execution-cnpj, D1).
    /// </summary>
    public static IServiceCollection AddD365ChangeFeed(this IServiceCollection services, Action<D365ChangeFeedOptions>? configure = null)
    {
        var options = new D365ChangeFeedOptions();
        configure?.Invoke(options);

        if (options.StampSettleMargin < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                $"Configuração inválida do feed do D365: StampSettleMargin deve ser de pelo menos 1s (veio {options.StampSettleMargin}).");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(options);   // o source reaproveita teto e tentativas de throttling
        AddShared(services);

        services.AddScoped<IDocumentChangeFeed>(sp => new D365ChangeFeed(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IConnectorProfileStore>(),
            sp.GetRequiredService<ID365TokenProvider>(),
            options,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<D365ChangeFeed>>()));

        // O teste da credencial gravada (D11): um provedor de client credentials próprio, porque o teste pede um token novo
        // a cada vez. Nunca é o do coletor, cujo cache fica intacto.
        services.TryAddSingleton(sp => new ClientCredentialsD365TokenProvider(
            sp.GetRequiredService<ISecretStore>(), sp.GetRequiredService<ILogger<ClientCredentialsD365TokenProvider>>()));
        services.AddScoped<IConnectorCredentialTest>(sp => new D365CredentialTest(
            sp.GetRequiredService<ClientCredentialsD365TokenProvider>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        return services;
    }

    /// <summary>
    /// Registra o <c>IInboundSource&lt;GoodsInvoice&gt;</c> do D365 (origem <c>Dynamics365</c>, scoped: lê o perfil do
    /// tenant) ao lado dos demais sources — a esteira escolhe pela origem da referência (ADR-0025). O cache de
    /// cadastros é singleton, para sobreviver entre mensagens. Requer o <c>ISecretStore</c> e o <c>IConnectorProfileStore</c>;
    /// usa o <c>IProcessingTrace</c> registrado (sem ele, trace desligado).
    /// </summary>
    public static IServiceCollection AddD365GoodsInvoiceSource(this IServiceCollection services, Action<D365AssemblyOptions>? configure = null)
    {
        var options = new D365AssemblyOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessingTrace, NoOpProcessingTrace>();
        AddShared(services);
        services.TryAddSingleton(sp => new D365ReferenceDataCache(options, sp.GetRequiredService<TimeProvider>()));

        services.AddScoped<IInboundSource<GoodsInvoice>>(sp => new D365GoodsInvoiceSource(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IConnectorProfileStore>(),
            sp.GetRequiredService<ID365TokenProvider>(),
            sp.GetService<D365ChangeFeedOptions>() ?? new D365ChangeFeedOptions(),
            sp.GetRequiredService<D365ReferenceDataCache>(),
            sp.GetRequiredService<IProcessingTrace>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<D365GoodsInvoiceSource>>()));

        return services;
    }

    /// <summary>
    /// Registra o <c>ICompanyDirectory</c> do D365 (origem <c>Dynamics365</c>, scoped: lê o perfil do tenant), sobre o cadastro
    /// de estabelecimentos (change erp-company-directory-and-card-filters, D1 e D2). Usa o token do coletor e as opções de
    /// throttling do feed, quando registradas. Requer o <c>ISecretStore</c> e o <c>IConnectorProfileStore</c>.
    /// </summary>
    public static IServiceCollection AddD365CompanyDirectory(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        AddShared(services);

        services.AddScoped<ICompanyDirectory>(sp => new D365CompanyDirectory(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IConnectorProfileStore>(),
            sp.GetRequiredService<ID365TokenProvider>(),
            sp.GetService<D365ChangeFeedOptions>() ?? new D365ChangeFeedOptions(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<D365CompanyDirectory>>()));

        return services;
    }

    /// <summary>
    /// Registra o <c>IDocumentDiscovery</c> do D365 (origem <c>Dynamics365</c>, scoped: lê o perfil do tenant), a descoberta por
    /// período da integração manual e da agendada (change erp-company-directory-and-card-filters, D4). Usa o token do coletor
    /// e as opções de throttling do feed, quando registradas. Requer o <c>ISecretStore</c> e o <c>IConnectorProfileStore</c>.
    /// </summary>
    public static IServiceCollection AddD365DocumentDiscovery(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        AddShared(services);

        services.AddScoped<IDocumentDiscovery>(sp => new D365DocumentDiscovery(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IConnectorProfileStore>(),
            sp.GetRequiredService<ID365TokenProvider>(),
            sp.GetService<D365ChangeFeedOptions>() ?? new D365ChangeFeedOptions(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<D365DocumentDiscovery>>()));

        return services;
    }

    private static void AddShared(IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName);

        // Singleton de propósito: as credenciais (e o cache de token delas) precisam sobreviver entre passadas.
        services.TryAddSingleton<ID365TokenProvider>(sp => new ClientCredentialsD365TokenProvider(
            sp.GetRequiredService<ISecretStore>(), sp.GetRequiredService<ILogger<ClientCredentialsD365TokenProvider>>()));
    }
}
