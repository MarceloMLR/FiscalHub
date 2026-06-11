using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Domain.Envelope;
using Xunit;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Prova que o núcleo da esteira funciona sem Azure nem plataforma real: as três portas são
/// substituídas por implementações falsas. Documento usado é um dummy, pois o pipeline é genérico.
/// </summary>
public class DocumentPipelineTests
{
    private sealed record TestDocument(string Id);

    [Fact]
    public async Task New_document_is_fetched_submitted_and_recorded()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore { AlreadySubmitted = false };
        var pipeline = new DocumentPipeline<TestDocument>(source, dispatcher, store);

        await pipeline.ProcessAsync(Reference(), Context());

        Assert.Equal(1, source.FetchCount);       // buscou o documento na origem
        Assert.Equal(1, dispatcher.SubmitCount);  // enviou ao destino
        Assert.Equal(1, store.RecordCount);       // registrou o resultado
        Assert.Equal("guid-123", store.Recorded!.ExternalId);
    }

    [Fact]
    public async Task Already_submitted_document_is_skipped()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore { AlreadySubmitted = true };
        var pipeline = new DocumentPipeline<TestDocument>(source, dispatcher, store);

        await pipeline.ProcessAsync(Reference(), Context());

        Assert.Equal(0, source.FetchCount);       // não buscou
        Assert.Equal(0, dispatcher.SubmitCount);  // não reenviou
        Assert.Equal(0, store.RecordCount);       // não registrou de novo
    }

    private static DocumentReference Reference() => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "nfe-key-1",
        Locator = "blob://nfe-key-1.xml",
    };

    private static DispatchContext Context() => new()
    {
        TenantId = "tenant-a",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private sealed class FakeSource : IInboundSource<TestDocument>
    {
        public string Origin => "fake";
        public int FetchCount { get; private set; }

        public Task<TestDocument> FetchAsync(DocumentReference reference, CancellationToken ct = default)
        {
            FetchCount++;
            return Task.FromResult(new TestDocument("doc-1"));
        }
    }

    private sealed class FakeDispatcher : IComplianceDispatcher<TestDocument>
    {
        public string Destination => "fake";
        public int SubmitCount { get; private set; }

        public Task<IntegrationReceipt> SubmitAsync(TestDocument document, DispatchContext context, CancellationToken ct = default)
        {
            SubmitCount++;
            return Task.FromResult(new IntegrationReceipt
            {
                ExternalId = "guid-123",
                Status = IntegrationStatus.Submitted,
            });
        }

        public Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
            => Task.FromResult(new IntegrationResult { Status = IntegrationStatus.Confirmed });
    }

    private sealed class FakeStore : IProcessingStore
    {
        public bool AlreadySubmitted { get; init; }
        public int RecordCount { get; private set; }
        public IntegrationReceipt? Recorded { get; private set; }

        public Task<bool> AlreadySubmittedAsync(string tenantId, string naturalKey, CancellationToken ct = default)
            => Task.FromResult(AlreadySubmitted);

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        {
            RecordCount++;
            Recorded = receipt;
            return Task.CompletedTask;
        }
    }
}
