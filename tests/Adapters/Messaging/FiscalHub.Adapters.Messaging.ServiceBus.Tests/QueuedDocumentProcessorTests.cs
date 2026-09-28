using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Especifica a lógica do consumidor: desserializa a referência da mensagem, monta o contexto e
/// chama o roteador (ADR-0025). O roteador é falso (IDocumentRouter stub) — sem Service Bus nem Azure.
/// </summary>
public class QueuedDocumentProcessorTests
{
    [Fact]
    public async Task Handle_deserializes_reference_and_invokes_the_router()
    {
        var router = new FakeRouter();
        var processor = new QueuedDocumentProcessor(router);
        BinaryData body = BinaryData.FromObjectAsJson(Reference(), DocumentQueueSerialization.Options);

        await processor.HandleAsync(body, "corr-42");

        Assert.NotNull(router.Reference);
        Assert.Equal("nfe-1", router.Reference!.NaturalKey);
        Assert.Equal(DocumentType.GoodsInvoice55, router.Reference.Type);
        Assert.Equal("nfe/nfe-1.xml", router.Reference.Locator);

        Assert.NotNull(router.Context);
        Assert.Equal("tenant-a", router.Context!.TenantId);
        Assert.Equal("nfe-1", router.Context.NaturalKey);
        Assert.Equal("corr-42", router.Context.CorrelationId);   // usa o correlationId da mensagem
    }

    [Fact]
    public async Task Origin_survives_the_round_trip_and_its_absence_stays_absent()
    {
        var router = new FakeRouter();
        var processor = new QueuedDocumentProcessor(router);

        await processor.HandleAsync(BinaryData.FromObjectAsJson(Reference() with { Origin = "Dynamics365" }, DocumentQueueSerialization.Options), "c");
        Assert.Equal("Dynamics365", router.Reference!.Origin);

        // Mensagem publicada antes do campo existir: continua válida, sem origem (cai no perfil do tenant).
        await processor.HandleAsync(BinaryData.FromString("""{"tenantId":"tenant-a","type":"GoodsInvoice55","naturalKey":"nfe-1","locator":"nfe/nfe-1.xml"}"""), "c");
        Assert.Null(router.Reference!.Origin);
    }

    [Fact]
    public async Task Discovered_group_survives_the_round_trip_and_its_absence_stays_absent()
    {
        var router = new FakeRouter();
        var processor = new QueuedDocumentProcessor(router);
        var metadata = new DocumentMetadata
        {
            CompanyCode = "44278225000260",
            BranchCode = "SP-01",
            ReferenceDate = new DateOnly(2026, 8, 7),
            DocumentNumber = "000123",
            DocumentModel = "SE",
        };

        BinaryData body = BinaryData.FromObjectAsJson(Reference() with { Metadata = metadata }, DocumentQueueSerialization.Options);
        Assert.Contains("\"referenceDate\":\"2026-08-07\"", body.ToString());   // o dia, sem hora e sem fuso
        await processor.HandleAsync(body, "c");
        Assert.Equal(metadata, router.Reference!.Metadata);

        // Mensagem publicada antes do campo existir: continua válida, sem o grupo.
        await processor.HandleAsync(BinaryData.FromString("""{"tenantId":"tenant-a","type":"GoodsInvoice55","naturalKey":"nfe-1","locator":"nfe/nfe-1.xml"}"""), "c");
        Assert.Null(router.Reference!.Metadata);
    }

    [Fact]
    public async Task Handle_generates_correlation_id_when_message_has_none()
    {
        var router = new FakeRouter();
        var processor = new QueuedDocumentProcessor(router);
        BinaryData body = BinaryData.FromObjectAsJson(Reference(), DocumentQueueSerialization.Options);

        await processor.HandleAsync(body, correlationId: null);

        Assert.False(string.IsNullOrWhiteSpace(router.Context!.CorrelationId));
    }

    [Fact]
    public async Task Handle_throws_on_empty_message_body()
    {
        var processor = new QueuedDocumentProcessor(new FakeRouter());

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

    private sealed class FakeRouter : IDocumentRouter
    {
        public DocumentReference? Reference { get; private set; }
        public DispatchContext? Context { get; private set; }

        public Task RouteAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default)
        {
            Reference = reference;
            Context = context;
            return Task.CompletedTask;
        }
    }
}
