using Azure.Messaging.ServiceBus;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Enfileira a referência (claim-check) numa fila do Service Bus. O corpo é o JSON da
/// <see cref="DocumentReference"/>; o documento pesado continua no Blob.
/// </summary>
internal sealed class ServiceBusDocumentQueue : IDocumentQueue
{
    private readonly ServiceBusSender _sender;

    public ServiceBusDocumentQueue(ServiceBusClient client, IOptions<ServiceBusOptions> options)
        => _sender = client.CreateSender(options.Value.QueueName);

    public async Task EnqueueAsync(DocumentReference reference, CancellationToken ct = default)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(reference, DocumentQueueSerialization.Options))
        {
            ContentType = "application/json",
            MessageId = $"{reference.TenantId}:{reference.NaturalKey}",
            CorrelationId = Guid.NewGuid().ToString(),
        };

        await _sender.SendMessageAsync(message, ct);
    }
}
