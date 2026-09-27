using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Roteamento por tipo (spec discovery-queue-consumer): só a NF-e 55 vai para a esteira; o resto sai como
/// "ignorado" gravado, sem tocar a origem. Fora do escopo constatado na montagem também vira "ignorado".
/// </summary>
public class DocumentRouterTests
{
    [Fact]
    public async Task Goods_invoice_goes_to_the_pipeline_with_its_context()
    {
        var pipeline = new FakePipeline();
        var store = new FakeStore();
        var router = new DocumentRouter(pipeline, store);

        await router.RouteAsync(Reference(DocumentType.GoodsInvoice55), Context());

        Assert.Equal("brmf|A", pipeline.Processed.Single().Reference.NaturalKey);
        Assert.Equal("corr-1", pipeline.Processed.Single().Context.CorrelationId);
        Assert.Empty(store.Ignored);
    }

    [Theory]
    [InlineData(DocumentType.ServiceNfse)]
    [InlineData(DocumentType.Transport57)]
    public async Task Other_types_are_recorded_as_ignored_without_calling_the_pipeline(DocumentType type)
    {
        var pipeline = new FakePipeline();
        var store = new FakeStore();
        var router = new DocumentRouter(pipeline, store);

        await router.RouteAsync(Reference(type), Context());

        Assert.Empty(pipeline.Processed);
        (string key, string reason) = Assert.Single(store.Ignored);
        Assert.Equal("brmf|A", key);
        Assert.Equal($"ignorado: tipo fora do escopo ({type})", reason);
    }

    [Fact]
    public async Task Out_of_scope_found_while_fetching_is_recorded_as_ignored_and_not_rethrown()
    {
        var pipeline = new FakePipeline { Throw = new DocumentOutOfScopeException("ignorado: tipo fora do escopo (modelo SE)") };
        var store = new FakeStore();
        var router = new DocumentRouter(pipeline, store);

        await router.RouteAsync(Reference(DocumentType.GoodsInvoice55), Context());

        (string key, string reason) = Assert.Single(store.Ignored);
        Assert.Equal("brmf|A", key);
        Assert.Equal("ignorado: tipo fora do escopo (modelo SE)", reason);
    }

    [Fact]
    public async Task Any_other_failure_propagates_to_the_transport()
    {
        var pipeline = new FakePipeline { Throw = new HttpRequestException("F&O fora") };
        var store = new FakeStore();
        var router = new DocumentRouter(pipeline, store);

        await Assert.ThrowsAsync<HttpRequestException>(() => router.RouteAsync(Reference(DocumentType.GoodsInvoice55), Context()));

        Assert.Empty(store.Ignored);
    }

    private static DocumentReference Reference(DocumentType type) => new()
    {
        TenantId = "tenant-a",
        Type = type,
        NaturalKey = "brmf|A",
        Locator = "d365/brmf/5637148912",
        Origin = "Dynamics365",
    };

    private static DispatchContext Context() => new()
    {
        TenantId = "tenant-a",
        NaturalKey = "brmf|A",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private sealed class FakePipeline : IDocumentPipeline<GoodsInvoice>
    {
        public Exception? Throw { get; init; }

        public List<(DocumentReference Reference, DispatchContext Context)> Processed { get; } = [];

        public Task ProcessAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Processed.Add((reference, context));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IProcessingStore
    {
        public List<(string Key, string Reason)> Ignored { get; } = [];

        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            Ignored.Add((reference.NaturalKey, reason));
            return Task.CompletedTask;
        }

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
