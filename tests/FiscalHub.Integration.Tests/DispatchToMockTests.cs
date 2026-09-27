using System.Net;
using System.Text;
using System.Text.Json;
using FiscalHub.Adapters.Ingress.D365Poll;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalHub.Integration.Tests;

/// <summary>
/// Ponta a ponta com o contrato novo (ADR-0026, design D13 e D15): nota D365 GRAVADA → esteira com o validador real →
/// dispatcher REAL da Avalara → mock de compliance em memória → poll de status. O que chega ao mock é o payload que a
/// plataforma receberia. O store é falso e registra o que a esteira e o poll lhe entregam; a preservação da observação
/// na confirmação é do SqlProcessingStore, provada nos testes dele.
/// </summary>
public class DispatchToMockTests
{
    private const string OutgoingKey = "brmf|BRMF21-10000026";
    private const string ImportKey = "brmf|BRMF06-110000031";

    [Fact]
    public async Task Recorded_d365_note_is_sent_with_the_new_contract_and_confirmed()
    {
        using var h = new Harness();
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        Assert.Empty(row.Receipt!.Omissions);

        using JsonDocument payload = await h.SentPayloadAsync(row.Receipt.ExternalId);
        JsonElement root = payload.RootElement;
        Assert.Equal("20247332000182", root.GetProperty("codigoEmpresa").GetString());      // da configuração, não do ERP
        Assert.Equal("20247332000182", root.GetProperty("codigoContribuinte").GetString());
        Assert.Equal("Southridge Video Brasil Ltda", root.GetProperty("parceiro").GetProperty("nome").GetString());
        Assert.Equal(OutgoingKey, root.GetProperty("codigoReferenciaIntegracao").GetString());
        Assert.All(root.GetProperty("itens").EnumerateArray(), item =>
        {
            JsonElement imposto = item.GetProperty("imposto");
            Assert.True(imposto.TryGetProperty("icms", out _));
            Assert.True(imposto.TryGetProperty("pis", out _));
            Assert.True(imposto.TryGetProperty("cofins", out _));
            Assert.False(item.TryGetProperty("impostos", out _));   // nota de 2016: sem grupo da Reforma, sem array
        });
        Assert.Equal(51, root.GetProperty("itens")[0].GetProperty("imposto").GetProperty("ipi").GetProperty("situacaoTributariaIPI").GetInt32());

        await h.PollAsync();

        Assert.Equal((IntegrationStatus.Confirmed, (string?)null), h.Store.Polled[OutgoingKey]);
    }

    [Fact]
    public async Task Import_note_is_sent_with_the_import_tax_in_its_block_and_the_charge_declared_as_omitted()
    {
        using var h = new Harness();
        h.ServeNote("35637156586", withAccounting: true, "postaladdress-22565428565", "city-22565694955", "postaladdress-22565426303");

        await h.ProcessAsync(ImportKey, "35637156586");

        StoredRow row = h.Store.Rows[ImportKey];
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        Assert.Equal(["item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)"], row.Receipt!.Omissions);

        using JsonDocument payload = await h.SentPayloadAsync(row.Receipt.ExternalId);
        JsonElement ii = payload.RootElement.GetProperty("itens")[0].GetProperty("imposto").GetProperty("ii");
        Assert.Equal(4500m, ii.GetProperty("baseCalculoII").GetDecimal());    // a TaxBase, completada pela contábil (D9)
        Assert.Equal(30m, ii.GetProperty("aliquotaII").GetDecimal());
        Assert.Equal(1350m, ii.GetProperty("valorII").GetDecimal());
        Assert.False(payload.RootElement.GetProperty("parceiro").TryGetProperty("cnpj", out _));   // fornecedor estrangeiro

        await h.PollAsync();

        // A confirmação volta sem motivo novo — o store mantém a observação do envio (SqlProcessingStoreTests).
        Assert.Equal((IntegrationStatus.Confirmed, (string?)null), h.Store.Polled[ImportKey]);
    }

    [Fact]
    public async Task Platform_error_on_status_is_recorded_with_the_platform_reason()
    {
        using var h = new Harness();
        await h.SetMockResultAsync("erro", "CFOP 6101 incompatível com a operação");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");
        await h.PollAsync();

        Assert.Equal(
            (IntegrationStatus.IntegrationError, "Plataforma de compliance rejeitou: CFOP 6101 incompatível com a operação"),
            h.Store.Polled[OutgoingKey]);
    }

    [Fact]
    public async Task Platform_refusal_on_submission_is_recorded_with_the_platform_reason_after_a_single_post()
    {
        using var h = new Harness();
        await h.SetMockResultAsync("rejeitar", "codigoEmpresa não cadastrado");
        h.ServeNote("35637156582", "postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.ProcessAsync(OutgoingKey, "35637156582");

        StoredRow row = h.Store.Rows[OutgoingKey];
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.Equal("Plataforma de compliance recusou: codigoEmpresa não cadastrado", row.Reason);
        Assert.Equal(1, h.MockPosts);
    }

    // ---------- apoio ----------

    private static string Fixture(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relative));

    private sealed class Harness : IDisposable
    {
        private readonly WebApplicationFactory<Program> _mock = new();
        private readonly ServiceProvider _services;
        private readonly DocumentPipeline<GoodsInvoice> _pipeline;
        private readonly StatusPoller<GoodsInvoice> _poller;
        private readonly CountingHandler _toMock;

        public Harness()
        {
            var profiles = new Profiles();
            var trace = new NoTrace();
            _toMock = new CountingHandler(_mock.Server.CreateHandler());

            var services = new ServiceCollection();
            services.AddSingleton<IConnectorProfileStore>(profiles);
            services.AddSingleton<IProcessingTrace>(trace);
            services.AddAvalaraComplianceDispatcher(o => o.BaseUrl = "http://localhost/");
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => _toMock));
            _services = services.BuildServiceProvider();
            var dispatcher = _services.GetRequiredService<IComplianceDispatcher<GoodsInvoice>>();

            var d365 = new D365GoodsInvoiceSource(
                new HttpClient(Http), profiles, new FakeTokens(), new D365ChangeFeedOptions(),
                new D365ReferenceDataCache(new D365AssemblyOptions(), TimeProvider.System), trace, TimeProvider.System,
                NullLogger<D365GoodsInvoiceSource>.Instance);
            _pipeline = new DocumentPipeline<GoodsInvoice>(
                new InboundSourceResolver<GoodsInvoice>([d365], profiles), new GoodsInvoiceValidator(), dispatcher, Store, trace,
                new GoodsInvoiceMetadataExtractor());
            _poller = new StatusPoller<GoodsInvoice>(Store, dispatcher, new StatusPollerOptions());
        }

        public SequencedHttp Http { get; } = new();

        public RecordingStore Store { get; } = new();

        public int MockPosts => _toMock.Posts;

        public void ServeNote(string recId, params string[] reference) => ServeNote(recId, withAccounting: false, reference);

        public void ServeNote(string recId, bool withAccounting, params string[] reference)
        {
            foreach (string file in new[] { "header", "lines", "taxes", "charges" })
            {
                Http.Respond(Fixture(Path.Combine("d365", "notes", recId, $"{file}.json")));
            }

            if (withAccounting)
            {
                Http.Respond(Fixture(Path.Combine("d365", "notes", recId, "taxtrans.json")));
            }

            foreach (string file in reference)
            {
                Http.Respond(Fixture(Path.Combine("d365", "reference", $"{file}.json")));
            }
        }

        public Task ProcessAsync(string naturalKey, string recId)
            => _pipeline.ProcessAsync(
                new DocumentReference
                {
                    TenantId = "tenant-a",
                    Type = DocumentType.GoodsInvoice55,
                    NaturalKey = naturalKey,
                    Locator = $"d365/brmf/{recId}",
                    Origin = "Dynamics365",
                },
                new DispatchContext { TenantId = "tenant-a", NaturalKey = naturalKey, CorrelationId = "corr-1", Operation = DocumentStatus.Issued });

        public Task<int> PollAsync() => _poller.PollOnceAsync();

        /// <summary>O JSON exato que o mock recebeu (a inspeção do próprio mock).</summary>
        public async Task<JsonDocument> SentPayloadAsync(string externalId)
            => JsonDocument.Parse(await _mock.CreateClient().GetStringAsync($"documents/{externalId}"));

        public async Task SetMockResultAsync(string result, string reason)
        {
            using HttpResponseMessage response = await _mock.CreateClient().PostAsync($"admin/result/{result}?motivo={Uri.EscapeDataString(reason)}", null);
            response.EnsureSuccessStatusCode();
        }

        public void Dispose()
        {
            _services.Dispose();
            _mock.Dispose();
        }
    }

    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Posts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class SequencedHttp : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new();

        public void Respond(string json) => _bodies.Enqueue(json);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = _bodies.Count > 0 ? _bodies.Dequeue() : throw new InvalidOperationException($"Requisição inesperada: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeTokens : ID365TokenProvider
    {
        public Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default) => Task.FromResult("tok");
    }

    private sealed class Profiles : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantConnectorProfile?>(new TenantConnectorProfile
            {
                TenantId = tenantId,
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = "Dynamics365",
                InboundSettings = """{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"]}""",
                OutboundAdapter = "Avalara",
                // Sem baseUrl: vale a das options (o mock em memória). A Contoso traduzida para a empresa do JSON real.
                OutboundSettings = """{"sandbox":{"establishments":{"44278225000180":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"}}}}""",
            });

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }

    internal sealed record StoredRow(IntegrationStatus Status, IntegrationReceipt? Receipt, string? Reason);

    /// <summary>Registra o que a esteira e o poll entregam ao store; lista como pendente o que foi enviado.</summary>
    internal sealed class RecordingStore : IProcessingStore
    {
        public Dictionary<string, StoredRow> Rows { get; } = [];

        public Dictionary<string, (IntegrationStatus Status, string? Reason)> Polled { get; } = [];

        public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        {
            Rows[reference.NaturalKey] = new StoredRow(receipt.Status, receipt, null);
            return Task.CompletedTask;
        }

        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        {
            Rows[reference.NaturalKey] = new StoredRow(IntegrationStatus.IntegrationError, null, reason);
            return Task.CompletedTask;
        }

        public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PendingIntegration>>(
            [
                .. Rows.Where(r => r.Value.Status == IntegrationStatus.Submitted && !Polled.ContainsKey(r.Key))
                       .Select(r => new PendingIntegration { TenantId = "tenant-a", NaturalKey = r.Key, ExternalId = r.Value.Receipt!.ExternalId, Attempts = 0 }),
            ]);

        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default)
        {
            Polled[naturalKey] = (status, reason);
            return Task.CompletedTask;
        }
    }

    private sealed class NoTrace : IProcessingTrace
    {
        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;
    }
}
