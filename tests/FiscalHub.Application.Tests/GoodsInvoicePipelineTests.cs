using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Tests;

/// <summary>
/// A linha do que o hub julga, na esteira com o validador real (ADR-0026): conteúdo fiscal segue para o envio;
/// só a nota sem item para antes.
/// </summary>
public class GoodsInvoicePipelineTests
{
    [Fact]
    public async Task Invoice_whose_item_has_no_reform_group_reaches_the_dispatcher()
    {
        GoodsInvoice invoice = Invoice([Item() with { ReformTaxes = null }]);
        var dispatcher = new CountingDispatcher();
        var store = new CountingStore();

        await Pipeline(invoice, dispatcher, store).ProcessAsync(Reference(), Context());

        Assert.Equal(1, dispatcher.SubmitCount);
        Assert.Equal(1, store.SubmissionCount);
        Assert.Empty(store.Rejections);
    }

    [Fact]
    public async Task Invoice_without_items_is_rejected_and_never_reaches_the_dispatcher()
    {
        var dispatcher = new CountingDispatcher();
        var store = new CountingStore();

        await Pipeline(Invoice([]), dispatcher, store).ProcessAsync(Reference(), Context());

        Assert.Equal(0, dispatcher.SubmitCount);
        Assert.Equal(["A nota não possui itens."], store.Rejections);
    }

    private static DocumentPipeline<GoodsInvoice> Pipeline(GoodsInvoice invoice, CountingDispatcher dispatcher, CountingStore store)
        => new(new FixedResolver(invoice), new GoodsInvoiceValidator(), dispatcher, store, new NoTrace(), new FixedExtractor());

    private static GoodsInvoice Invoice(IReadOnlyList<GoodsInvoiceItem> items) => new()
    {
        AccessKey = "",   // chave vazia também não é julgada pelo hub
        Model = "55",
        Series = "1",
        Number = "1",
        IssueDate = new DateTimeOffset(2016, 9, 2, 12, 0, 0, TimeSpan.Zero),
        Issuer = new Party { TaxId = "11613525000119", Name = "Proseware Brasil Ltda" },
        Recipient = new Party { TaxId = "44278225000180", Name = "Contoso Entertainment System Brazil" },
        TotalAmount = 1000m,
        Items = items,
    };

    private static GoodsInvoiceItem Item() => new()
    {
        Number = 1,
        ProductCode = "BRMF810",
        Description = "Material de limpeza",
        Ncm = "00000000",
        Cfop = "1556",
        Quantity = 1m,
        UnitAmount = 1000m,
        TotalAmount = 1000m,
    };

    private static DocumentReference Reference() => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "brmf|BRMF06-110000027",
        Locator = "d365/brmf/5637156579",
    };

    private static DispatchContext Context() => new()
    {
        TenantId = "tenant-a",
        NaturalKey = "brmf|BRMF06-110000027",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private sealed class FixedResolver(GoodsInvoice invoice) : IInboundSourceResolver<GoodsInvoice>, IInboundSource<GoodsInvoice>
    {
        public string Origin => "fake";

        public Task<IInboundSource<GoodsInvoice>> ResolveAsync(DocumentReference reference, CancellationToken ct = default)
            => Task.FromResult<IInboundSource<GoodsInvoice>>(this);

        public Task<FetchResult<GoodsInvoice>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
            => Task.FromResult(new FetchResult<GoodsInvoice> { Document = invoice, ContentHash = "hash-1" });
    }

    private sealed class FixedExtractor : IDocumentMetadataExtractor<GoodsInvoice>
    {
        public DocumentMetadata Extract(GoodsInvoice document) => new()
        {
            CompanyCode = "44278225",
            BranchCode = "0001",
            ReferenceDate = new DateOnly(2016, 9, 2),
            DocumentNumber = document.Number,
            DocumentModel = document.Model,
        };
    }

    private sealed class CountingDispatcher : IComplianceDispatcher<GoodsInvoice>
    {
        public string Destination => "fake";
        public int SubmitCount { get; private set; }

        public Task<IntegrationReceipt> SubmitAsync(GoodsInvoice document, DispatchContext context, CancellationToken ct = default)
        {
            SubmitCount++;
            return Task.FromResult(new IntegrationReceipt { ExternalId = "guid-1", Status = IntegrationStatus.Submitted });
        }

        public Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
            => Task.FromResult(new IntegrationResult { Status = IntegrationStatus.Confirmed });
    }

    private sealed class CountingStore : IProcessingStore
    {
        public int SubmissionCount { get; private set; }
        public List<string> Rejections { get; } = [];

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        {
            SubmissionCount++;
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

    private sealed class NoTrace : IProcessingTrace
    {
        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;
    }
}
