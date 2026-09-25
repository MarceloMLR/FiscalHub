using Azure.Messaging.ServiceBus;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>Registro no DI do adapter de mensageria (Service Bus). Único ponto público.</summary>
public static class ServiceBusMessagingServiceCollectionExtensions
{
    /// <summary>Chave do <c>IDocumentQueue</c> da fila de descoberta (<see cref="AddServiceBusDiscoveryQueue"/>).</summary>
    public const string DiscoveryQueueKey = "discovery";

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

        services.AddSingleton<IDocumentQueue>(sp => new ServiceBusDocumentQueue(
            sp.GetRequiredService<ServiceBusClient>(),
            sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName));
        services.AddScoped<QueuedDocumentProcessor>();
        services.AddScoped<DeadLetterHandler>();
        services.AddHostedService<ServiceBusTriggerService>();
        services.AddHostedService<DeadLetterTriggerService>();

        return services;
    }

    /// <summary>
    /// Registra a fila de descoberta (ADR-0024) como <c>IDocumentQueue</c> com a chave
    /// <see cref="DiscoveryQueueKey"/> — só o envio, <b>sem consumidor</b>: quem consome é a fatia de
    /// roteamento/montagem. Reusa o <c>ServiceBusClient</c> de <see cref="AddServiceBusDocumentQueue"/>,
    /// que precisa ser chamado antes.
    /// </summary>
    public static IServiceCollection AddServiceBusDiscoveryQueue(this IServiceCollection services, Action<ServiceBusDiscoveryQueueOptions>? configure = null)
    {
        var options = new ServiceBusDiscoveryQueueOptions();
        configure?.Invoke(options);

        services.AddKeyedSingleton<IDocumentQueue>(DiscoveryQueueKey, (sp, _) => new ServiceBusDocumentQueue(
            sp.GetRequiredService<ServiceBusClient>(), options.QueueName));

        return services;
    }
}
