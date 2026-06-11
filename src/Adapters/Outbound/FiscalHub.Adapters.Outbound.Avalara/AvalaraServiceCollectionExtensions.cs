using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Registro no DI do adapter Avalara. Único ponto público — os tipos do adapter ficam internal.</summary>
public static class AvalaraServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>IComplianceDispatcher&lt;GoodsInvoice&gt;</c> da Avalara como typed client
    /// (<c>IHttpClientFactory</c>), com a URL base vinda de <see cref="AvalaraOptions"/> e o gancho
    /// de token no-op. Substitua o token provider por uma implementação real numa fatia futura.
    /// </summary>
    public static IServiceCollection AddAvalaraComplianceDispatcher(
        this IServiceCollection services,
        Action<AvalaraOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<IAvalaraTokenProvider, NoOpAvalaraTokenProvider>();

        services.AddHttpClient<IComplianceDispatcher<GoodsInvoice>, AvalaraComplianceDispatcher>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AvalaraOptions>>().Value;
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress))
            {
                throw new InvalidOperationException(
                    $"AvalaraOptions.BaseUrl ausente ou inválida: '{options.BaseUrl}'. Configure uma URL absoluta.");
            }

            client.BaseAddress = baseAddress;
        });

        return services;
    }
}
