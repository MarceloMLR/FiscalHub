using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Prova que o núcleo da esteira funciona sem Azure nem plataforma real: as portas são
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
        var store = new FakeStore { AlreadyProcessed = false };
        var trace = new RecordingTrace();
        var pipeline = new DocumentPipeline<TestDocument>(new FakeResolver(source), new FakeValidator(), dispatcher, store, trace, new FakeExtractor());

        await pipeline.ProcessAsync(Reference(), Context());

        Assert.Equal(1, source.FetchCount);       // buscou o documento na origem
        Assert.Equal(1, dispatcher.SubmitCount);  // enviou ao destino
        Assert.Equal(1, store.RecordCount);       // registrou o resultado
        Assert.Equal("guid-123", store.Recorded!.ExternalId);
        Assert.Equal("nfe-key-1", trace.DomainKey); // fotografou o domínio
    }

    [Fact]
    public async Task Duplicate_content_is_skipped()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore { AlreadyProcessed = true };   // mesmo cru já processado
        var trace = new RecordingTrace();
        var pipeline = new DocumentPipeline<TestDocument>(new FakeResolver(source), new FakeValidator(), dispatcher, store, trace, new FakeExtractor());

        await pipeline.ProcessAsync(Reference(), Context());

        Assert.Equal(1, source.FetchCount);       // buscou pra conhecer o cru (é ele que decide)
        Assert.Equal(0, dispatcher.SubmitCount);  // não reenviou (conteúdo idêntico)
        Assert.Equal(0, store.RecordCount);       // não registrou de novo
        Assert.Null(trace.DomainKey);             // parou na idempotência, antes da foto de domínio
    }

    [Fact]
    public async Task Manual_reload_reprocesses_even_if_already_submitted()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore { AlreadyProcessed = true };   // nota já confirmada
        var trace = new RecordingTrace();
        var pipeline = new DocumentPipeline<TestDocument>(new FakeResolver(source), new FakeValidator(), dispatcher, store, trace, new FakeExtractor());

        await pipeline.ProcessAsync(Reference() with { Trigger = IngestionTrigger.Manual }, Context());

        Assert.Equal(1, source.FetchCount);       // buscou de novo (furou a idempotência)
        Assert.Equal(1, dispatcher.SubmitCount);  // reenviou de propósito
        Assert.Equal(1, store.RecordCount);       // registrou o reenvio
    }

    [Fact]
    public async Task Invalid_document_is_rejected_and_not_submitted()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore { AlreadyProcessed = false };
        var trace = new RecordingTrace();
        var pipeline = new DocumentPipeline<TestDocument>(new FakeResolver(source), new FakeValidator { Valid = false }, dispatcher, store, trace, new FakeExtractor());

        await pipeline.ProcessAsync(Reference(), Context());

        Assert.Equal(1, source.FetchCount);       // buscou
        Assert.Equal(0, dispatcher.SubmitCount);  // NÃO enviou
        Assert.Equal(0, store.RecordCount);       // não registrou envio
        Assert.Equal(1, store.RejectionCount);    // registrou a rejeição
        Assert.Equal("nfe-key-1", trace.DomainKey); // fotografou o domínio mesmo rejeitando
    }

    [Fact]
    public async Task Rejection_discovered_at_dispatch_is_recorded_with_its_reason_and_not_retried()
    {
        // A plataforma recusou, ou o conector não conseguiu montar a requisição: desfecho registrado, sem exceção
        // (retentativa não conserta conteúdo nem configuração) — mesmo caminho da rejeição na validação.
        var dispatcher = new FakeDispatcher { Rejection = new DispatchRejectedException("Plataforma de compliance recusou: codigoEmpresa não cadastrado") };
        var store = new FakeStore();
        var pipeline = new DocumentPipeline<TestDocument>(new FakeResolver(new FakeSource()), new FakeValidator(), dispatcher, store, new RecordingTrace(), new FakeExtractor());

        await pipeline.ProcessAsync(Reference(), Context());

        Assert.Equal(1, dispatcher.SubmitCount);
        Assert.Equal(0, store.RecordCount);
        Assert.Equal(["Plataforma de compliance recusou: codigoEmpresa não cadastrado"], store.Rejections);
    }

    [Fact]
    public async Task Transient_dispatch_failure_still_propagates_for_the_native_retry()
    {
        var dispatcher = new FakeDispatcher { Rejection = new HttpRequestException("503") };
        var store = new FakeStore();
        var pipeline = new DocumentPipeline<TestDocument>(new FakeResolver(new FakeSource()), new FakeValidator(), dispatcher, store, new RecordingTrace(), new FakeExtractor());

        await Assert.ThrowsAsync<HttpRequestException>(() => pipeline.ProcessAsync(Reference(), Context()));

        Assert.Empty(store.Rejections);
        Assert.Equal(0, store.RecordCount);
    }

    [Fact]
    public async Task Document_is_fetched_from_the_source_resolved_for_its_reference()
    {
        var xml = new FakeSource();
        var d365 = new FakeSource();
        var resolver = new FakeResolver(xml) { ByOrigin = { ["Dynamics365"] = d365 } };
        var pipeline = new DocumentPipeline<TestDocument>(resolver, new FakeValidator(), new FakeDispatcher(), new FakeStore(), new RecordingTrace(), new FakeExtractor());

        await pipeline.ProcessAsync(Reference() with { Origin = "Dynamics365" }, Context());

        Assert.Equal(1, d365.FetchCount);   // buscou no source da origem da referência
        Assert.Equal(0, xml.FetchCount);
    }

    [Fact]
    public async Task Resolution_failure_propagates_before_touching_store_or_trace()
    {
        var store = new FakeStore();
        var trace = new RecordingTrace();
        var pipeline = new DocumentPipeline<TestDocument>(new FailingResolver(), new FakeValidator(), new FakeDispatcher(), store, trace, new FakeExtractor());

        await Assert.ThrowsAsync<InboundSourceNotFoundException>(() => pipeline.ProcessAsync(Reference(), Context()));

        Assert.Equal(0, store.RecordCount);
        Assert.Equal(0, store.RejectionCount);
        Assert.Null(trace.SourceKey);
        Assert.Null(trace.DomainKey);
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
        NaturalKey = "nfe-key-1",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private sealed class FakeSource : IInboundSource<TestDocument>
    {
        public string Origin => "fake";

        public string? CheckLocator(DocumentReference reference) => null;
        public int FetchCount { get; private set; }

        public Task<FetchResult<TestDocument>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
        {
            FetchCount++;
            return Task.FromResult(new FetchResult<TestDocument> { Document = new TestDocument("doc-1"), ContentHash = "hash-1" });
        }
    }

    /// <summary>Resolve pela origem da referência; sem origem mapeada, o source padrão.</summary>
    private sealed class FakeResolver(FakeSource fallback) : IInboundSourceResolver<TestDocument>
    {
        public Dictionary<string, FakeSource> ByOrigin { get; } = [];

        public Task<IInboundSource<TestDocument>> ResolveAsync(DocumentReference reference, CancellationToken ct = default)
            => Task.FromResult<IInboundSource<TestDocument>>(
                reference.Origin is { } origin && ByOrigin.TryGetValue(origin, out FakeSource? source) ? source : fallback);
    }

    private sealed class FailingResolver : IInboundSourceResolver<TestDocument>
    {
        public Task<IInboundSource<TestDocument>> ResolveAsync(DocumentReference reference, CancellationToken ct = default)
            => throw new InboundSourceNotFoundException("sem adapter para a origem");
    }

    private sealed class FakeValidator : IDocumentValidator<TestDocument>
    {
        public bool Valid { get; init; } = true;

        public ValidationResult Validate(TestDocument document)
            => Valid ? ValidationResult.Valid() : ValidationResult.Invalid(["problema de teste"]);
    }

    private sealed class FakeExtractor : IDocumentMetadataExtractor<TestDocument>
    {
        public DocumentMetadata Extract(TestDocument document) => new()
        {
            CompanyCode = "12345678",
            BranchCode = "0001",
            ReferenceDate = new DateOnly(2026, 7, 23),
            DocumentNumber = "1",
            DocumentModel = "55",
        };
    }

    private sealed class FakeDispatcher : IComplianceDispatcher<TestDocument>
    {
        public string Destination => "fake";
        public int SubmitCount { get; private set; }
        public Exception? Rejection { get; init; }

        public Task<IntegrationReceipt> SubmitAsync(TestDocument document, DispatchContext context, CancellationToken ct = default)
        {
            SubmitCount++;
            if (Rejection is not null)
                throw Rejection;

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
        public bool AlreadyProcessed { get; init; }
        public int RecordCount { get; private set; }
        public int RejectionCount => Rejections.Count;
        public List<string> Rejections { get; } = [];
        public IntegrationReceipt? Recorded { get; private set; }

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default)
            => Task.FromResult(AlreadyProcessed);

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        {
            RecordCount++;
            Recorded = receipt;
            return Task.CompletedTask;
        }

        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            Rejections.Add(reason);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PendingIntegration>>([]);

        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingTrace : IProcessingTrace
    {
        public string? SourceKey { get; private set; }
        public string? DomainKey { get; private set; }
        public string? OutboundKey { get; private set; }

        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default)
        {
            SourceKey = naturalKey;
            return Task.CompletedTask;
        }

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        {
            DomainKey = naturalKey;
            return Task.CompletedTask;
        }

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        {
            OutboundKey = naturalKey;
            return Task.CompletedTask;
        }
    }
}
