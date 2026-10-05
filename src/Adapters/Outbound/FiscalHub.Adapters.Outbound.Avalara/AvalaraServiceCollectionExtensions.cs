using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Registro no DI do adapter Avalara. Único ponto público — os tipos do adapter ficam internal.</summary>
public static class AvalaraServiceCollectionExtensions
{
    /// <summary>O nome do adapter, como o perfil o grava: o do teste de credencial e o da listagem.</summary>
    internal const string AdapterName = "Avalara";

    internal const string ListingClientName = "avalara-listing";

    private const string TokenClientName = "avalara-token";

    /// <summary>
    /// Registra o <c>IComplianceDispatcher&lt;GoodsInvoice&gt;</c> da Avalara como typed client (<c>IHttpClientFactory</c>),
    /// autenticado por padrão: o token vem da credencial do tenant no ambiente ativo, com o segredo lido do
    /// <see cref="ISecretStore"/> (ADR-0027). Os clientes HTTP não têm URL base — toda URI é absoluta, da seção do tenant.
    /// Salvar o perfil do tenant esquece a recusa lembrada e os tokens dele (<see cref="IConnectorProfileObserver"/>).
    /// </summary>
    public static IServiceCollection AddAvalaraComplianceDispatcher(
        this IServiceCollection services,
        Action<AvalaraOptions>? configure = null)
    {
        OptionsBuilder<AvalaraOptions> options = services.AddOptions<AvalaraOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        // Zero ou negativo impede o host de subir: com o $top 0, a primeira página viria vazia, e a conta pareceria não ter
        // empresas; com o teto 0, nenhuma listagem terminaria.
        options
            .Validate(o => o.ListingPageSize > 0, "Avalara:ListingPageSize precisa ser positivo: é o $top de cada página da listagem.")
            .Validate(o => o.ListingMaxPages > 0, "Avalara:ListingMaxPages precisa ser positivo: é o teto de páginas por lista da listagem.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessingTrace, NoOpProcessingTrace>();

        // Nenhum valor de cabeçalho no log do IHttpClientFactory, em nenhum nível: o Bearer e o que a plataforma devolver.
        services.AddHttpClient(TokenClientName).RedactLoggedHeaders(_ => true);

        // Singleton de propósito: o cache de token e a recusa lembrada precisam viver o processo.
        services.TryAddSingleton<IAvalaraTokenProvider>(sp => new AvalaraTokenProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TokenClientName),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<IOptions<AvalaraOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<AvalaraTokenProvider>>()));
        services.AddSingleton<IConnectorProfileObserver, AvalaraProfileObserver>();
        services.AddSingleton<IConnectorCredentialTest, AvalaraCredentialTest>();   // o botão de testar a credencial (D10)

        // A Avalara sabe listar os estabelecimentos da plataforma: o de/para sem cadastro manual (platform-establishment-resolution).
        // Singleton com o cliente nomeado, como o provider de token; quem guarda a listagem é o resolvedor do núcleo.
        services.AddHttpClient(ListingClientName).RedactLoggedHeaders(_ => true);
        services.AddSingleton<IPlatformEstablishmentListing>(sp => new AvalaraEstablishmentListing(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ListingClientName),
            sp.GetRequiredService<IAvalaraTokenProvider>(),
            sp.GetRequiredService<IOptions<AvalaraOptions>>(),
            sp.GetRequiredService<ILogger<AvalaraEstablishmentListing>>()));

        services.AddHttpClient<IComplianceDispatcher<GoodsInvoice>, AvalaraComplianceDispatcher>().RedactLoggedHeaders(_ => true);

        return services;
    }

    /// <summary>
    /// Troca o token real por "sem token": as requisições saem sem autorização, e nenhuma credencial é lida. Só por pedido
    /// explícito, para um teste contra um destino que não autentica — o host da aplicação nunca chama. Loga um aviso ao
    /// compor.
    /// </summary>
    public static IServiceCollection UseAvalaraWithoutAuthentication(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAvalaraTokenProvider>(sp =>
        {
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AvalaraServiceCollectionExtensions).FullName!).LogWarning(
                "Adapter Avalara composto SEM autenticação (UseAvalaraWithoutAuthentication): os envios e as consultas saem sem token.");
            return new NoOpAvalaraTokenProvider();
        }));

        return services;
    }
}

/// <summary>Salvar o perfil do tenant esquece a recusa lembrada e os tokens dele, na hora (ADR-0027).</summary>
internal sealed class AvalaraProfileObserver(IAvalaraTokenProvider tokens) : IConnectorProfileObserver
{
    public Task ProfileSavedAsync(string tenantId, CancellationToken ct = default)
    {
        tokens.Forget(tenantId);
        return Task.CompletedTask;
    }
}
