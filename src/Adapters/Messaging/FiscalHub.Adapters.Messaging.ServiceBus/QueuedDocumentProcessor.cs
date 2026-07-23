using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Messaging.ServiceBus;

/// <summary>
/// Lógica do consumidor, separada da casca do Service Bus para ser testável: desserializa a
/// referência da mensagem, monta o contexto e chama a esteira. Uma falha aqui propaga — o Service
/// Bus reconta a entrega e, no limite, manda pra dead-letter (ADR-0004/0008).
/// </summary>
internal sealed class QueuedDocumentProcessor
{
    private readonly IDocumentPipeline<GoodsInvoice> _pipeline;

    public QueuedDocumentProcessor(IDocumentPipeline<GoodsInvoice> pipeline) => _pipeline = pipeline;

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

        await _pipeline.ProcessAsync(reference, context, ct);
    }
}
