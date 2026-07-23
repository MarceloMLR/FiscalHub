using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Especifica a lógica do consumidor: desserializa a referência da mensagem, monta o contexto e
/// chama a esteira. A esteira é falsa (IDocumentPipeline stub) — sem Service Bus nem Azure.
/// </summary>
public class QueuedDocumentProcessorTests
{
    [Fact]
    public async Task Handle_deserializes_reference_and_invokes_pipeline()
    {
        var pipeline = new FakePipeline();
        var processor = new QueuedDocumentProcessor(pipeline);
        BinaryData body = BinaryData.FromObjectAsJson(Reference(), DocumentQueueSerialization.Options);

        await processor.HandleAsync(body, "corr-42");

        Assert.NotNull(pipeline.Reference);
        Assert.Equal("nfe-1", pipeline.Reference!.NaturalKey);
        Assert.Equal(DocumentType.GoodsInvoice55, pipeline.Reference.Type);
        Assert.Equal("nfe/nfe-1.xml", pipeline.Reference.Locator);

        Assert.NotNull(pipeline.Context);
        Assert.Equal("tenant-a", pipeline.Context!.TenantId);
        Assert.Equal("nfe-1", pipeline.Context.NaturalKey);
        Assert.Equal("corr-42", pipeline.Context.CorrelationId);   // usa o correlationId da mensagem
    }

    [Fact]
    public async Task Handle_generates_correlation_id_when_message_has_none()
    {
        var pipeline = new FakePipeline();
        var processor = new QueuedDocumentProcessor(pipeline);
        BinaryData body = BinaryData.FromObjectAsJson(Reference(), DocumentQueueSerialization.Options);

        await processor.HandleAsync(body, correlationId: null);

        Assert.False(string.IsNullOrWhiteSpace(pipeline.Context!.CorrelationId));
    }

    [Fact]
    public async Task Handle_throws_on_empty_message_body()
    {
        var processor = new QueuedDocumentProcessor(new FakePipeline());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.HandleAsync(BinaryData.FromString("null"), "corr-1"));
    }

    private static DocumentReference Reference() => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "nfe-1",
        Locator = "nfe/nfe-1.xml",
    };

    private sealed class FakePipeline : IDocumentPipeline<GoodsInvoice>
    {
        public DocumentReference? Reference { get; private set; }
        public DispatchContext? Context { get; private set; }

        public Task ProcessAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default)
        {
            Reference = reference;
            Context = context;
            return Task.CompletedTask;
        }
    }
}
