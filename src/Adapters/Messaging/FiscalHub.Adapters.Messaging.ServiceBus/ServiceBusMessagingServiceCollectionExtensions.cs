using Azure.Messaging.ServiceBus;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>Registro no DI do adapter de mensageria (Service Bus). Único ponto público.</summary>
public static class ServiceBusMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>IDocumentQueue</c> (enfileira) e o gatilho consumidor sobre uma fila do
    /// Service Bus. O consumidor chama a esteira via <c>IDocumentPipeline&lt;GoodsInvoice&gt;</c>.
    /// </summary>
    public static IServiceCollection AddServiceBusDocumentQueue(this IServiceCollection services, Action<ServiceBusOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton(sp =>
        {
            ServiceBusOptions options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return new ServiceBusClient(options.ConnectionString);
        });

        services.AddSingleton<IDocumentQueue, ServiceBusDocumentQueue>();
        services.AddScoped<QueuedDocumentProcessor>();
        services.AddHostedService<ServiceBusTriggerService>();

        return services;
    }
}
