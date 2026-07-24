using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Discovery.Local;

/// <summary>Registro no DI da descoberta local (dev). Único ponto público.</summary>
public static class DocumentDiscoveryServiceCollectionExtensions
{
    /// <summary>Registra o <c>IDocumentDiscovery</c> de dev, com catálogo fixo casando os seeds.</summary>
    public static IServiceCollection AddLocalDocumentDiscovery(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentDiscovery, LocalDocumentDiscovery>();
        return services;
    }
}
