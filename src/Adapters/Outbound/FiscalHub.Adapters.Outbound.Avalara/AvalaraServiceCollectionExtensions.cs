using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Registro no DI do adapter Avalara. Único ponto público — os tipos do adapter ficam internal.</summary>
public static class AvalaraServiceCollectionExtensions
{
    private const string TokenClientName = "avalara-token";

    /// <summary>
    /// Registra o <c>IComplianceDispatcher&lt;GoodsInvoice&gt;</c> da Avalara como typed client
    /// (<c>IHttpClientFactory</c>), com a URL base vinda de <see cref="AvalaraOptions"/>. Por padrão
    /// não autentica; chame <see cref="AddAvalaraTokenProvider"/> para usar o token real.
    /// </summary>
    public static IServiceCollection AddAvalaraComplianceDispatcher(
        this IServiceCollection services,
        Action<AvalaraOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<IAvalaraTokenProvider, NoOpAvalaraTokenProvider>();

        services.AddHttpClient<IComplianceDispatcher<GoodsInvoice>, AvalaraComplianceDispatcher>(
            (sp, client) => client.BaseAddress = ResolveBaseAddress(sp));

        return services;
    }

    /// <summary>
    /// Substitui o gancho no-op pelo provedor de token real (OAuth client credentials), com cache
    /// por tenant e renovação com margem. Sem esta chamada o dispatcher segue sem autenticação —
    /// o que é conveniente contra o mock local, que não exige token.
    /// </summary>
    public static IServiceCollection AddAvalaraTokenProvider(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(TokenClientName, (sp, client) => client.BaseAddress = ResolveBaseAddress(sp));

        // Singleton de propósito: o cache de token precisa sobreviver entre as chamadas — um
        // provider por processo. Se fosse transitório (padrão dos typed clients), cada resolução
        // criaria um cache novo e o token seria buscado toda vez.
        services.Replace(ServiceDescriptor.Singleton<IAvalaraTokenProvider>(sp => new AvalaraTokenProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TokenClientName),
            sp.GetRequiredService<IOptions<AvalaraOptions>>(),
            sp.GetRequiredService<TimeProvider>())));

        return services;
    }

    private static Uri ResolveBaseAddress(IServiceProvider sp)
    {
        AvalaraOptions options = sp.GetRequiredService<IOptions<AvalaraOptions>>().Value;

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseAddress))
        {
            throw new InvalidOperationException(
                $"AvalaraOptions.BaseUrl ausente ou inválida: '{options.BaseUrl}'. Configure uma URL absoluta.");
        }

        return baseAddress;
    }
}
