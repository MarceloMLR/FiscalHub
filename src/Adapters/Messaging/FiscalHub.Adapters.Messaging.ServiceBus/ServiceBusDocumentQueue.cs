using Azure.Messaging.ServiceBus;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Enfileira a referência (claim-check) numa fila do Service Bus. O corpo é o JSON da
/// <see cref="DocumentReference"/>; o documento pesado continua no Blob. A fila é por parâmetro: a de
/// entrada da esteira (<c>documents-in</c>) e a de descoberta (<c>documents-discovered</c>) usam a mesma classe.
/// </summary>
internal sealed class ServiceBusDocumentQueue : IDocumentQueue
{
    private readonly ServiceBusSender _sender;

    public ServiceBusDocumentQueue(ServiceBusClient client, string queueName)
        => _sender = client.CreateSender(queueName);

    /// <summary>Fila para a qual esta instância publica.</summary>
    public string QueueName => _sender.EntityPath;

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
