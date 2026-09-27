using Azure.Messaging.ServiceBus;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>Registro no DI do adapter de mensageria (Service Bus). Único ponto público.</summary>
public static class ServiceBusMessagingServiceCollectionExtensions
{
    /// <summary>Chave do <c>IDocumentQueue</c> da fila de descoberta (<see cref="AddServiceBusDiscoveryQueue"/>).</summary>
    public const string DiscoveryQueueKey = "discovery";

    /// <summary>
    /// Registra o <c>IDocumentQueue</c> (enfileira) e o gatilho consumidor sobre uma fila do
    /// Service Bus. O consumidor chama o roteador (<c>IDocumentRouter</c>, ADR-0025), que leva à esteira.
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
        AddConsumer(services, sp => sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value.QueueName);

        return services;
    }

    /// <summary>
    /// Registra a fila de descoberta (ADR-0024) como <c>IDocumentQueue</c> com a chave
    /// <see cref="DiscoveryQueueKey"/> — para onde o feed de mudanças publica — e o consumidor e a dead-letter
    /// dela, na mesma casca da fila de entrada (ADR-0025): cada referência vai ao roteador, que monta o documento
    /// na esteira ou grava "ignorado". Reusa o <c>ServiceBusClient</c> de <see cref="AddServiceBusDocumentQueue"/>,
    /// que precisa ser chamado antes.
    /// </summary>
    public static IServiceCollection AddServiceBusDiscoveryQueue(this IServiceCollection services, Action<ServiceBusDiscoveryQueueOptions>? configure = null)
    {
        var options = new ServiceBusDiscoveryQueueOptions();
        configure?.Invoke(options);

        services.AddKeyedSingleton<IDocumentQueue>(DiscoveryQueueKey, (sp, _) => new ServiceBusDocumentQueue(
            sp.GetRequiredService<ServiceBusClient>(), options.QueueName));
        AddConsumer(services, _ => options.QueueName);

        return services;
    }

    /// <summary>
    /// Consumidor e dead-letter de uma fila. Registro direto como <c>IHostedService</c>, e não por
    /// <c>AddHostedService</c>: este usa <c>TryAddEnumerable</c> e descartaria a segunda instância do mesmo tipo.
    /// </summary>
    private static void AddConsumer(IServiceCollection services, Func<IServiceProvider, string> queueName)
    {
        services.AddSingleton<IHostedService>(sp => new ServiceBusTriggerService(
            sp.GetRequiredService<ServiceBusClient>(), sp, queueName(sp), sp.GetRequiredService<ILogger<ServiceBusTriggerService>>()));
        services.AddSingleton<IHostedService>(sp => new DeadLetterTriggerService(
            sp.GetRequiredService<ServiceBusClient>(), sp, queueName(sp), sp.GetRequiredService<ILogger<DeadLetterTriggerService>>()));
    }
}
