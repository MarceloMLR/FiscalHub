using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Messaging.ServiceBus.Tests;

/// <summary>
/// Especifica o consumidor de dead-letter: desserializa a referência e registra o documento como
/// não-processável, com o motivo. O store é falso.
/// </summary>
public class DeadLetterHandlerTests
{
    [Fact]
    public async Task Handle_records_dead_letter_with_reason()
    {
        var store = new FakeStore();
        var handler = new DeadLetterHandler(store);
        BinaryData body = BinaryData.FromObjectAsJson(Reference(), DocumentQueueSerialization.Options);

        await handler.HandleAsync(body, "MaxDeliveryCountExceeded");

        Assert.NotNull(store.DeadLettered);
        Assert.Equal("nfe-1", store.DeadLettered!.Value.Key);
        Assert.Equal("MaxDeliveryCountExceeded", store.DeadLettered.Value.Reason);
    }

    [Fact]
    public async Task Discovery_queue_reference_is_recorded_as_dead_lettered_too()
    {
        // Referência publicada pelo feed do D365 na documents-discovered: a mesma dead-letter visível (ADR-0010).
        var store = new FakeStore();
        var handler = new DeadLetterHandler(store);
        DocumentReference discovered = Reference() with { NaturalKey = "brmf|BRMF21-10000026", Locator = "d365/brmf/35637156582", Origin = "Dynamics365" };

        await handler.HandleAsync(BinaryData.FromObjectAsJson(discovered, DocumentQueueSerialization.Options), "MaxDeliveryCountExceeded");

        Assert.Equal(("brmf|BRMF21-10000026", "MaxDeliveryCountExceeded"), store.DeadLettered);
    }

    [Fact]
    public async Task Handle_throws_on_empty_body()
    {
        var handler = new DeadLetterHandler(new FakeStore());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(BinaryData.FromString("null"), "reason"));
    }

    private static DocumentReference Reference() => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "nfe-1",
        Locator = "nfe/nfe-1.xml",
    };

    private sealed class FakeStore : IProcessingStore
    {
        public (string Key, string Reason)? DeadLettered { get; private set; }

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            DeadLettered = (reference.NaturalKey, reason);
            return Task.CompletedTask;
        }

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default) => Task.FromResult(false);
        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PendingIntegration>>([]);
        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }
}
