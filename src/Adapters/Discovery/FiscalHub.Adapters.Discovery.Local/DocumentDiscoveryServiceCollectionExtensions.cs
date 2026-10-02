using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Discovery.Local;

/// <summary>Registro no DI da descoberta local (dev). Único ponto público.</summary>
public static class DocumentDiscoveryServiceCollectionExtensions
{
    /// <summary>
    /// SÓ DESENVOLVIMENTO: registra o catálogo fixo dos XMLs de exemplo como o fallback de desenvolvimento da descoberta por
    /// período (change erp-company-directory-and-card-filters, D3), com a chave
    /// <see cref="InboundAdapterChoice.DevelopmentFallbackKey"/>, e não como implementação comum. Ele atende a integração
    /// manual e a agendada só do tenant cujo ERP não tem descoberta própria, e o reprocesso da nota de exemplo quando a
    /// descoberta do ERP não a acha. O host chama isto apenas em <c>IsDevelopment()</c>.
    /// </summary>
    public static IServiceCollection UseLocalDocumentDiscoveryAsDevelopmentFallback(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IDocumentDiscovery, LocalDocumentDiscovery>(InboundAdapterChoice.DevelopmentFallbackKey);
        return services;
    }
}
