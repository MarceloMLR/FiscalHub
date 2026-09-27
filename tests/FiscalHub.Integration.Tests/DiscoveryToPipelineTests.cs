using System.Net;
using System.Text;
using System.Text.Json;
using FiscalHub.Adapters.Inbound.Xml;
using FiscalHub.Adapters.Ingress.D365Poll;
using FiscalHub.Adapters.Messaging.ServiceBus;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalHub.Integration.Tests;

/// <summary>
/// Ponta a ponta sem Azure (ADR-0025): mensagem da fila → consumidor → roteador → esteira com resolver → sources
/// REAIS — o do D365 sobre as respostas gravadas do fiscosysdev, o do XML sobre um Blob falso. Store, trace e
/// despachante são falsos; validador e extrator de metadados são os reais.
/// </summary>
public class DiscoveryToPipelineTests
{
    private const string OutgoingKey = "brmf|BRMF21-10000026";
    private const string XmlAccessKey = "35260612345678000190550010000001231000000123";

    [Fact]
    public async Task Discovered_goods_invoice_reaches_the_pipeline_assembled_and_is_rejected_for_the_missing_reform_group()
    {
        var h = new Harness();
        h.ServeOutgoingNote(withReferenceData: true);

        await h.DeliverAsync(D365Reference(origin: "Dynamics365"));

        // Chegou montada: a foto do domínio tem os 3 itens, e o item 1 os 4 impostos da nota gravada.
        using JsonDocument domain = JsonDocument.Parse(h.Trace.Domain[OutgoingKey]);
        JsonElement items = domain.RootElement.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(4, items[0].GetProperty("taxes").GetArrayLength());

        // Desfecho real da base: nota de 2016, sem IBS/CBS → rejeitada na validação, nada enviado.
        Assert.Equal("Rejected", h.Store.Rows[OutgoingKey].Status);
        Assert.Contains("Item 1: tributos da Reforma (IBS/CBS) ausentes.", h.Store.Rows[OutgoingKey].Reason);
        Assert.Empty(h.Dispatcher.Submitted);
    }

    [Fact]
    public async Task Discovered_service_invoice_is_ignored_without_any_http_request()
    {
        var h = new Harness();

        await h.DeliverAsync(D365Reference(origin: "Dynamics365") with { Type = DocumentType.ServiceNfse, NaturalKey = "brmf|BRMF21-10000006", Locator = "d365/brmf/5637149826" });

        Assert.Equal("Ignored", h.Store.Rows["brmf|BRMF21-10000006"].Status);
        Assert.Equal("ignorado: tipo fora do escopo (ServiceNfse)", h.Store.Rows["brmf|BRMF21-10000006"].Reason);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Tenant_a_with_xml_and_d365_in_the_same_run_sends_each_to_its_own_source()
    {
        var h = new Harness();
        h.ServeOutgoingNote(withReferenceData: true);

        await h.DeliverAsync(XmlReference());                            // pela documents-in, do drop
        await h.DeliverAsync(D365Reference(origin: "Dynamics365"));      // pela documents-discovered, do feed

        Assert.Equal(["nfe/nfe-exemplo.xml"], h.Blob.Locators);         // o XML só foi ao Blob
        Assert.Equal(XmlAccessKey, h.Dispatcher.Submitted.Single().AccessKey);   // e, válido, foi enviado
        Assert.Equal(8, h.Http.Requests.Count);                          // o D365 só foi ao F&O
        Assert.All(h.Http.Requests, r => Assert.StartsWith("https://fiscosysdev.operations.dynamics.com/data/", r.RequestUri!.ToString()));
        Assert.Equal("Rejected", h.Store.Rows[OutgoingKey].Status);
    }

    [Fact]
    public async Task Message_without_origin_falls_back_to_the_tenant_profile()
    {
        var h = new Harness();   // perfil do tenant-a: Dynamics365
        h.ServeOutgoingNote(withReferenceData: true);

        await h.DeliverAsync(D365Reference(origin: null));

        Assert.NotEmpty(h.Http.Requests);
        Assert.Empty(h.Blob.Locators);
        Assert.Equal("Rejected", h.Store.Rows[OutgoingKey].Status);
    }

    [Fact]
    public async Task Same_reference_twice_without_change_gives_the_same_fingerprint()
    {
        var h = new Harness();
        h.ServeOutgoingNote(withReferenceData: true);
        h.ServeOutgoingNote(withReferenceData: false);   // cadastros já no cache

        await h.DeliverAsync(D365Reference(origin: "Dynamics365"));
        await h.DeliverAsync(D365Reference(origin: "Dynamics365"));

        Assert.Equal(2, h.Store.Hashes[OutgoingKey].Count);
        Assert.Equal(h.Store.Hashes[OutgoingKey][0], h.Store.Hashes[OutgoingKey][1]);
    }

    // ---------- apoio ----------

    private static DocumentReference D365Reference(string? origin) => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = OutgoingKey,
        Locator = "d365/brmf/35637156582",
        Origin = origin,
    };

    private static DocumentReference XmlReference() => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = XmlAccessKey,
        Locator = "nfe/nfe-exemplo.xml",
        Origin = "Xml",
    };

    private static string Fixture(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relative));

    private sealed class Harness
    {
        private readonly QueuedDocumentProcessor _processor;

        public Harness()
        {
            var profiles = new Profiles();
            var xml = new XmlGoodsInvoiceSource(Blob, new NfeXmlParser(), Trace);
            var d365 = new D365GoodsInvoiceSource(
                new HttpClient(Http), profiles, new FakeTokens(), new D365ChangeFeedOptions(),
                new D365ReferenceDataCache(new D365AssemblyOptions(), TimeProvider.System), Trace, TimeProvider.System,
                NullLogger<D365GoodsInvoiceSource>.Instance);
            var pipeline = new DocumentPipeline<GoodsInvoice>(
                new InboundSourceResolver<GoodsInvoice>([xml, d365], profiles),
                new GoodsInvoiceValidator(), Dispatcher, Store, Trace, new GoodsInvoiceMetadataExtractor());
            _processor = new QueuedDocumentProcessor(new DocumentRouter(pipeline, Store));
        }

        public SequencedHttp Http { get; } = new();
        public FakeBlob Blob { get; } = new(Fixture(Path.Combine("xml", "nfe-com-reforma.xml")));
        public RecordingTrace Trace { get; } = new();
        public InMemoryStore Store { get; } = new();
        public RecordingDispatcher Dispatcher { get; } = new();

        /// <summary>Entrega a referência como o Service Bus entrega: o JSON da mensagem que o publicador gravou.</summary>
        public Task DeliverAsync(DocumentReference reference)
            => _processor.HandleAsync(BinaryData.FromObjectAsJson(reference, DocumentQueueSerialization.Options), "corr-1");

        public void ServeOutgoingNote(bool withReferenceData)
        {
            foreach (string file in new[] { "header", "lines", "taxes", "charges" })
            {
                Http.Respond(Fixture(Path.Combine("d365", "notes", "35637156582", $"{file}.json")));
            }

            if (withReferenceData)
            {
                foreach (string file in new[] { "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958" })
                {
                    Http.Respond(Fixture(Path.Combine("d365", "reference", $"{file}.json")));
                }
            }
        }
    }

    private sealed class SequencedHttp : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Respond(string json) => _bodies.Enqueue(json);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string body = _bodies.Count > 0 ? _bodies.Dequeue() : throw new InvalidOperationException($"Requisição inesperada: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeTokens : ID365TokenProvider
    {
        public Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default) => Task.FromResult("tok");
    }

    private sealed class FakeBlob(string xml) : IBlobReader
    {
        public List<string> Locators { get; } = [];

        public Task<string> ReadTextAsync(string locator, CancellationToken ct = default)
        {
            Locators.Add(locator);
            return Task.FromResult(xml);
        }
    }

    private sealed class Profiles : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantConnectorProfile?>(new TenantConnectorProfile
            {
                TenantId = tenantId,
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = "Dynamics365",   // como o seed de dev do tenant-a
                InboundSettings = """{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"]}""",
                OutboundAdapter = "Avalara",
            });

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }

    internal sealed class InMemoryStore : IProcessingStore
    {
        public Dictionary<string, (string Status, string? Reason, string? Hash)> Rows { get; } = [];

        public Dictionary<string, List<string>> Hashes { get; } = [];

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default)
            => Task.FromResult(Rows.TryGetValue(naturalKey, out var row) && row.Hash == contentHash && row.Status is "Submitted" or "Confirmed");

        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default)
        {
            Hashes.TryAdd(reference.NaturalKey, []);
            Hashes[reference.NaturalKey].Add(contentHash);
            Rows[reference.NaturalKey] = (Rows.TryGetValue(reference.NaturalKey, out var row) ? row.Status : "Pending", null, contentHash);
            return Task.CompletedTask;
        }

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
            => Set(reference, "Submitted", null);

        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Set(reference, "Rejected", reason);

        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Set(reference, "Ignored", reason);

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Set(reference, "DeadLettered", reason);

        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PendingIntegration>>([]);

        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default)
            => Task.CompletedTask;

        private Task Set(DocumentReference reference, string status, string? reason)
        {
            Rows[reference.NaturalKey] = (status, reason, Rows.TryGetValue(reference.NaturalKey, out var row) ? row.Hash : null);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingDispatcher : IComplianceDispatcher<GoodsInvoice>
    {
        public string Destination => "fake";

        public List<GoodsInvoice> Submitted { get; } = [];

        public Task<IntegrationReceipt> SubmitAsync(GoodsInvoice document, DispatchContext context, CancellationToken ct = default)
        {
            Submitted.Add(document);
            return Task.FromResult(new IntegrationReceipt { ExternalId = Guid.NewGuid().ToString(), Status = IntegrationStatus.Submitted });
        }

        public Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
            => Task.FromResult(new IntegrationResult { Status = IntegrationStatus.Confirmed });
    }

    internal sealed class RecordingTrace : IProcessingTrace
    {
        public Dictionary<string, string> Domain { get; } = [];

        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        {
            Domain[naturalKey] = json;
            return Task.CompletedTask;
        }

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;
    }
}
