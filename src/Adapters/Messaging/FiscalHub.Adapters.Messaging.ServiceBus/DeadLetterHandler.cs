using FiscalHub.Application.Inbound;
using FiscalHub.Application.Pipeline;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Consome a dead-letter: uma mensagem que esgotou as tentativas vira um registro visível do
/// documento como não-processável, com o motivo do Service Bus. Separado da casca pra ser testável.
/// </summary>
internal sealed class DeadLetterHandler
{
    private readonly IProcessingStore _store;

    public DeadLetterHandler(IProcessingStore store) => _store = store;

    public async Task HandleAsync(BinaryData body, string reason, CancellationToken ct = default)
    {
        DocumentReference reference = body.ToObjectFromJson<DocumentReference>(DocumentQueueSerialization.Options)
            ?? throw new InvalidOperationException("Mensagem de dead-letter sem referência de documento.");

        await _store.RecordDeadLetterAsync(reference, reason, ct);
    }
}
