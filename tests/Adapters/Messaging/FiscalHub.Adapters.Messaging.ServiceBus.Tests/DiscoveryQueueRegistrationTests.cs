using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Especifica o registro da fila de descoberta: <c>IDocumentQueue</c> por chave, apontando para a fila
/// própria, com consumidor e dead-letter próprios (ADR-0025), sem mexer na fila de entrada da esteira. Nada
/// conecta no bus: o cliente do Service Bus só abre conexão no primeiro envio, e as cascas não são iniciadas.
/// </summary>
public class DiscoveryQueueRegistrationTests
{
    private const string EmulatorConnection =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [Fact]
    public async Task Discovery_queue_is_keyed_and_points_to_its_own_queue()
    {
        await using ServiceProvider sp = Build(services => services.AddServiceBusDiscoveryQueue());

        var discovery = (ServiceBusDocumentQueue)sp.GetRequiredKeyedService<IDocumentQueue>(
            ServiceBusMessagingServiceCollectionExtensions.DiscoveryQueueKey);

        Assert.Equal("documents-discovered", discovery.QueueName);
    }

    [Fact]
    public async Task Unkeyed_queue_stays_the_pipeline_input_queue()
    {
        await using ServiceProvider sp = Build(services => services.AddServiceBusDiscoveryQueue());

        var input = (ServiceBusDocumentQueue)sp.GetRequiredService<IDocumentQueue>();

        Assert.Equal("documents-in", input.QueueName);
    }

    [Fact]
    public async Task Discovery_queue_name_is_configurable()
    {
        await using ServiceProvider sp = Build(services => services.AddServiceBusDiscoveryQueue(o => o.QueueName = "descoberta-dev"));

        var discovery = (ServiceBusDocumentQueue)sp.GetRequiredKeyedService<IDocumentQueue>(
            ServiceBusMessagingServiceCollectionExtensions.DiscoveryQueueKey);

        Assert.Equal("descoberta-dev", discovery.QueueName);
    }

    [Fact]
    public async Task Each_queue_gets_one_consumer_and_one_dead_letter_listener()
    {
        await using ServiceProvider sp = Build(services => services.AddServiceBusDiscoveryQueue());

        IHostedService[] hosted = [.. sp.GetServices<IHostedService>()];

        Assert.Equal(
            ["documents-in", "documents-discovered"],
            hosted.OfType<ServiceBusTriggerService>().Select(s => s.QueueName));
        Assert.Equal(
            ["documents-in", "documents-discovered"],
            hosted.OfType<DeadLetterTriggerService>().Select(s => s.QueueName));
        Assert.Equal(4, hosted.Length);
    }

    private static ServiceProvider Build(Action<IServiceCollection> extra)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddServiceBusDocumentQueue(o =>
        {
            o.ConnectionString = EmulatorConnection;
            o.QueueName = "documents-in";
        });
        extra(services);
        return services.BuildServiceProvider();
    }
}
