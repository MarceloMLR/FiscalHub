using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Especifica o registro da fila de descoberta: <c>IDocumentQueue</c> por chave, apontando para a fila
/// própria, sem consumidor, e sem mexer na fila de entrada da esteira. Nada conecta no bus: o cliente do
/// Service Bus só abre conexão no primeiro envio.
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
    public void Discovery_queue_adds_no_consumer()
    {
        var services = new ServiceCollection();
        services.AddServiceBusDocumentQueue(o => o.ConnectionString = EmulatorConnection);
        int hostedBefore = services.Count(d => d.ServiceType == typeof(IHostedService));

        services.AddServiceBusDiscoveryQueue();

        Assert.Equal(hostedBefore, services.Count(d => d.ServiceType == typeof(IHostedService)));
    }

    private static ServiceProvider Build(Action<IServiceCollection> extra)
    {
        var services = new ServiceCollection();
        services.AddServiceBusDocumentQueue(o =>
        {
            o.ConnectionString = EmulatorConnection;
            o.QueueName = "documents-in";
        });
        extra(services);
        return services.BuildServiceProvider();
    }
}
