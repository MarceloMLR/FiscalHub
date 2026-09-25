using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>Registro no DI do adapter de ingresso D365 (feed de mudanças). Único ponto público — os tipos do adapter ficam internal.</summary>
public static class D365PollServiceCollectionExtensions
{
    private const string HttpClientName = "d365-odata";

    /// <summary>
    /// Registra o <c>IDocumentChangeFeed</c> do D365 (scoped: lê o perfil do tenant pelo
    /// <c>IConnectorProfileStore</c>) com token por client credentials. Requer <c>IConfiguration</c> (resolve
    /// o <c>kv:</c>) e um <c>IConnectorProfileStore</c> registrados. Em desenvolvimento, chame
    /// <see cref="UseD365AzureCliToken"/> para usar a sessão do Azure CLI.
    /// </summary>
    public static IServiceCollection AddD365ChangeFeed(this IServiceCollection services, Action<D365ChangeFeedOptions>? configure = null)
    {
        var options = new D365ChangeFeedOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(HttpClientName);

        // Singleton de propósito: as credenciais (e o cache de token delas) precisam sobreviver entre passadas.
        services.TryAddSingleton<ID365TokenProvider>(sp => new ClientCredentialsD365TokenProvider(sp.GetRequiredService<IConfiguration>()));

        services.AddScoped<IDocumentChangeFeed>(sp => new D365ChangeFeed(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IConnectorProfileStore>(),
            sp.GetRequiredService<ID365TokenProvider>(),
            options,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<D365ChangeFeed>>()));

        return services;
    }

    /// <summary>
    /// SÓ DESENVOLVIMENTO: troca o client credentials pela sessão do Azure CLI do desenvolvedor
    /// (<c>az login</c>), para rodar local antes de a app registration existir. O host deve chamar isto
    /// apenas em <c>IsDevelopment()</c>.
    /// </summary>
    public static IServiceCollection UseD365AzureCliToken(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<ID365TokenProvider>(sp => new AzureCliD365TokenProvider(sp.GetRequiredService<TimeProvider>())));
        return services;
    }
}
