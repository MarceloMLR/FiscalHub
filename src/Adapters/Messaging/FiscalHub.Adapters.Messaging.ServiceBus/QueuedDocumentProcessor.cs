using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Lógica do consumidor, separada da casca do Service Bus para ser testável: desserializa a
/// referência da mensagem, monta o contexto e chama o roteador, que leva à esteira do tipo ou grava o
/// desfecho "ignorado" (ADR-0025). Uma falha aqui propaga — o Service
/// Bus reconta a entrega e, no limite, manda pra dead-letter (ADR-0004/0008).
/// </summary>
internal sealed class QueuedDocumentProcessor
{
    private readonly IDocumentRouter _router;

    public QueuedDocumentProcessor(IDocumentRouter router) => _router = router;

    public async Task HandleAsync(BinaryData body, string? correlationId, CancellationToken ct = default)
    {
        DocumentReference reference = body.ToObjectFromJson<DocumentReference>(DocumentQueueSerialization.Options)
            ?? throw new InvalidOperationException("Mensagem da fila sem referência de documento.");

        var context = new DispatchContext
        {
            TenantId = reference.TenantId,
            NaturalKey = reference.NaturalKey,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId,
            Operation = DocumentStatus.Issued,
        };

        await _router.RouteAsync(reference, context, ct);
    }
}
