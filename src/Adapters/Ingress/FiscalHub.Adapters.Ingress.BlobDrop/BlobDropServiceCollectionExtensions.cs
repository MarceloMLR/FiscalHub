using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Ingress.BlobDrop;

/// <summary>Registro no DI do gatilho de ingestão por drop no Blob. Único ponto público.</summary>
public static class BlobDropServiceCollectionExtensions
{
    /// <summary>
    /// Registra o watcher da zona de drop. Requer um <c>BlobServiceClient</c> e um
    /// <c>IDocumentQueue</c> registrados. No cloud, troca-se por um gatilho de Event Grid.
    /// </summary>
    public static IServiceCollection AddBlobDropIngress(this IServiceCollection services, Action<BlobDropOptions>? configure = null)
    {
        services.Configure(configure ?? (_ => { }));
        services.AddHostedService<BlobDropWatcher>();
        return services;
    }
}
