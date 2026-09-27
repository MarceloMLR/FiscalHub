using FiscalHub.Application.Auth;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a ingestão manual (ADR-0028): a referência entra no tenant de quem está logado — o corpo não carrega
/// tenant —, e o locator passa pela regra da origem antes de enfileirar. Locator recusado não enfileira nada.
/// </summary>
public class ManualIngestionTests
{
    [Fact]
    public async Task Reference_goes_to_the_logged_tenant_with_the_xml_origin()
    {
        var queue = new RecordingQueue();
        var resolver = new FixedResolver(problem: null);
        var ingestion = new ManualIngestion(resolver, queue, new Tenant("tenant-b"));

        string? problem = await ingestion.EnqueueAsync("nfe-1", "nfe/tenant-b/nfe-1.xml");

        Assert.Null(problem);
        DocumentReference queued = Assert.Single(queue.References);
        Assert.Equal("tenant-b", queued.TenantId);
        Assert.Equal("Xml", queued.Origin);
        Assert.Equal("nfe-1", queued.NaturalKey);
        Assert.Equal("nfe/tenant-b/nfe-1.xml", queued.Locator);
        Assert.Equal(DocumentType.GoodsInvoice55, queued.Type);
        Assert.Equal("tenant-b", resolver.Checked!.TenantId);   // a regra do locator olha o tenant do login
    }

    [Fact]
    public async Task Refused_locator_returns_the_rule_and_enqueues_nothing()
    {
        var queue = new RecordingQueue();
        var ingestion = new ManualIngestion(new FixedResolver(problem: "o locator precisa estar no espaço do tenant."), queue, new Tenant("tenant-b"));

        string? problem = await ingestion.EnqueueAsync("nfe-1", "nfe/tenant-a/nfe-1.xml");

        Assert.Equal("o locator precisa estar no espaço do tenant.", problem);
        Assert.Empty(queue.References);
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class RecordingQueue : IDocumentQueue
    {
        public List<DocumentReference> References { get; } = [];

        public Task EnqueueAsync(DocumentReference reference, CancellationToken ct = default)
        {
            References.Add(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedResolver(string? problem) : IInboundSourceResolver<GoodsInvoice>, IInboundSource<GoodsInvoice>
    {
        public DocumentReference? Checked { get; private set; }

        public string Origin => "Xml";

        public Task<IInboundSource<GoodsInvoice>> ResolveAsync(DocumentReference reference, CancellationToken ct = default)
            => Task.FromResult<IInboundSource<GoodsInvoice>>(this);

        public string? CheckLocator(DocumentReference reference)
        {
            Checked = reference;
            return problem;
        }

        public Task<FetchResult<GoodsInvoice>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
            => throw new NotSupportedException("a ingestão manual não busca o documento");
    }
}
