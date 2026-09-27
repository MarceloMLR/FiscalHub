using System.Collections.Specialized;
using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging.Abstractions;
using static FiscalHub.Adapters.Ingress.D365Poll.Tests.D365Fixtures;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Source do D365 (spec d365-document-assembly): as consultas da montagem, a verificação do cabeçalho, a foto da
/// fonte e o cache de cadastros. O HTTP serve as respostas GRAVADAS do fiscosysdev, na ordem das requisições.
/// </summary>
public class D365GoodsInvoiceSourceTests
{
    private const string Env = "https://fiscosysdev.operations.dynamics.com";

    // ---------- consultas ----------

    [Fact]
    public async Task Four_queries_in_order_with_cross_company_select_and_the_two_term_tax_filter()
    {
        var h = new Harness();
        h.ServeNote(OutgoingNote);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026"));

        Assert.Equal(
            ["FSFiscalDocumentBRs", "FSFiscalDocumentLineBRs", "FSFiscalDocumentTaxTransBRs", "FSFiscalDocumentMiscChargeBRs",
             "FSPostalAddressBRs", "FSAddressCityBRs", "FSPostalAddressBRs", "FSAddressCityBRs"],
            h.Http.Requests.Select(EntitySet));
        Assert.All(h.Http.Requests, r => Assert.Equal("true", Query(r)["cross-company"]));
        Assert.Equal("FiscalDocumentRecId eq 35637156582", Query(h.Http.Requests[0])["$filter"]);
        Assert.Equal("FiscalDocumentRecId eq 35637156582", Query(h.Http.Requests[1])["$filter"]);
        Assert.Equal("FiscalDocumentRecId eq 35637156582 or MiscChargeFiscalDocumentRecId eq 35637156582", Query(h.Http.Requests[2])["$filter"]);
        Assert.Equal("FiscalDocumentRecId eq 35637156582", Query(h.Http.Requests[3])["$filter"]);
        Assert.Equal(D365GoodsInvoiceSource.HeaderSelect, Query(h.Http.Requests[0])["$select"]);
        Assert.Equal(D365GoodsInvoiceSource.LineSelect, Query(h.Http.Requests[1])["$select"]);
        Assert.Equal(D365GoodsInvoiceSource.TaxSelect, Query(h.Http.Requests[2])["$select"]);
        Assert.Equal(D365GoodsInvoiceSource.PostalAddressSelect, Query(h.Http.Requests[4])["$select"]);
    }

    [Fact]
    public async Task No_filter_uses_an_enum_value()
    {
        var h = new Harness();
        h.ServeNote(ImportNote, withAccounting: true);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565426303");

        await h.Source.FetchAsync(Reference(ImportNote, "BRMF06-110000031"));

        Assert.All(h.Http.Requests, r => Assert.DoesNotContain("Microsoft.Dynamics", Query(r)["$filter"] ?? string.Empty));
    }

    [Fact]
    public async Task Zeroed_import_tax_triggers_one_accounting_query_by_voucher_and_company()
    {
        var h = new Harness();
        h.ServeNote(ImportNote, withAccounting: true);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565426303");   // terceiro sem cidade: sem consulta de cidade

        FetchResult<GoodsInvoice> result = await h.Source.FetchAsync(Reference(ImportNote, "BRMF06-110000031"));

        HttpRequestMessage accounting = h.Http.Requests[4];
        Assert.Equal("FSTaxTransBRs", EntitySet(accounting));
        Assert.Equal("Voucher eq 'BRMF06-110000031' and dataAreaId eq 'brmf'", Query(accounting)["$filter"]);
        Assert.Equal(8, h.Http.Requests.Count);
        Assert.Equal(1350m, result.Document.Items.Single().Taxes.Single(t => t.Kind == TaxKind.ImportTax).Amount);
        Assert.Null(result.Document.Recipient.MunicipalityCode);   // endereço com CityRecId 0
        // O endereço do fornecedor estrangeiro não tem número nem bairro: campo vazio fica ausente, não "".
        Assert.Equal(new Address { Street = "567 Apple Road", PostalCode = "89706" }, result.Document.Recipient.Address);
    }

    [Fact]
    public async Task Without_zeroed_import_tax_there_is_no_accounting_query()
    {
        var h = new Harness();
        h.ServeNote(OutgoingNote);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        await h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026"));

        Assert.DoesNotContain(h.Http.Requests, r => EntitySet(r) == "FSTaxTransBRs");
    }

    // ---------- verificação do cabeçalho ----------

    [Fact]
    public async Task Empty_header_fails_citing_company_rec_id_and_the_permission_hypothesis()
    {
        var h = new Harness();
        h.Http.Respond("""{"value":[]}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Source.FetchAsync(Reference(5637148912, "BRMF21-10000027")));

        Assert.Contains("brmf", ex.Message);
        Assert.Contains("5637148912", ex.Message);
        Assert.Contains("acesso", ex.Message);
        Assert.Single(h.Http.Requests);
    }

    [Fact]
    public async Task Natural_key_mismatch_fails_showing_both_keys()
    {
        var h = new Harness();
        h.Http.Respond(Text(Note(OutgoingNote, "header")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000031")));

        Assert.Contains("brmf|BRMF21-10000031", ex.Message);
        Assert.Contains("brmf|BRMF21-10000026", ex.Message);
        Assert.Single(h.Http.Requests);
    }

    [Fact]
    public async Task Service_note_is_out_of_scope_by_model_without_further_queries_or_photo()
    {
        var h = new Harness();
        h.Http.Respond(Text($"scope/{ServiceNote}/header.json"));

        var ex = await Assert.ThrowsAsync<DocumentOutOfScopeException>(() => h.Source.FetchAsync(Reference(ServiceNote, "BRMF21-10000006")));

        Assert.Contains("modelo SE", ex.Reason);
        Assert.Single(h.Http.Requests);
        Assert.Null(h.Trace.SourceContent);
    }

    [Fact]
    public async Task Cancelled_goods_invoice_is_out_of_scope_by_status()
    {
        // Derivada: a base não tem nota 55 cancelada (as 2 canceladas são 01). Cabeçalho gravado com Status trocado.
        JsonObject header = Editable(Rows(Note(OutgoingNote, "header")).Single());
        header["Status"] = "Cancelled";
        var h = new Harness();
        h.Http.Respond(Response(header));

        var ex = await Assert.ThrowsAsync<DocumentOutOfScopeException>(() => h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026")));

        Assert.Contains("Cancelled", ex.Reason);
        Assert.Single(h.Http.Requests);
    }

    [Fact]
    public async Task Recorded_cancelled_model_01_is_out_of_scope_by_model()
    {
        var h = new Harness();
        h.Http.Respond(Text($"scope/{CancelledModel01}/header.json"));

        var ex = await Assert.ThrowsAsync<DocumentOutOfScopeException>(() => h.Source.FetchAsync(Reference(CancelledModel01, "BRMF12-30000000")));

        Assert.Contains("modelo 01", ex.Reason);
    }

    // ---------- foto da fonte ----------

    [Fact]
    public async Task Source_photo_is_the_canonical_json_and_its_hash_is_the_content_hash()
    {
        var h = new Harness();
        h.ServeNote(OutgoingNote);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        FetchResult<GoodsInvoice> result = await h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026"));

        Assert.Equal("json", h.Trace.SourceFormat);
        Assert.Equal("brmf|BRMF21-10000026", h.Trace.SourceKey);
        Assert.StartsWith("{", h.Trace.SourceContent);
        Assert.Contains("\"v\": 2", h.Trace.SourceContent);   // v2: $select ampliado (connector-not-validator, D12)
        Assert.Equal(ContentFingerprint.Of(h.Trace.SourceContent!), result.ContentHash);
    }

    [Fact]
    public async Task Source_photo_is_already_saved_when_the_assembly_fails_on_divergence()
    {
        // Derivada: a linha contábil gravada da ponte do ImportTax com o tipo trocado para IPI.
        JsonObject accounting = Editable(Rows(Note(ImportNote, "taxtrans")).Single(a => a.GetProperty("TaxTransRecId").GetInt64() == 35637488268));
        accounting["TaxType"] = "IPI";
        var h = new Harness();
        h.ServeNote(ImportNote);
        h.Http.Respond(Response(accounting));
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565426303");

        await Assert.ThrowsAsync<D365AssemblyException>(() => h.Source.FetchAsync(Reference(ImportNote, "BRMF06-110000031")));

        Assert.NotNull(h.Trace.SourceContent);
        Assert.Contains("\"TaxType\": \"IPI\"", h.Trace.SourceContent);   // os dois lados na foto
        Assert.Contains("\"FiscalTaxType\": \"ImportTax\"", h.Trace.SourceContent);
    }

    // ---------- cadastros ----------

    [Fact]
    public async Task Second_note_with_the_same_parties_does_not_query_address_or_city_again()
    {
        var h = new Harness();
        h.ServeNote(SameCustomerNoteA);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565429296");   // cidade do terceiro = a do estabelecimento
        h.ServeNote(SameCustomerNoteB);

        FetchResult<GoodsInvoice> a = await h.Source.FetchAsync(Reference(SameCustomerNoteA, "BRMF21-10000027"));
        int afterFirst = h.Http.Requests.Count;
        FetchResult<GoodsInvoice> b = await h.Source.FetchAsync(Reference(SameCustomerNoteB, "BRMF12-30000001"));

        Assert.Equal(7, afterFirst);
        Assert.Equal(4, h.Http.Requests.Count - afterFirst);   // só cabeçalho, linhas, impostos, encargos
        Assert.Equal("3550308", b.Document.Recipient.MunicipalityCode);
        Assert.Equal(a.Document.Issuer.MunicipalityCode, b.Document.Issuer.MunicipalityCode);
    }

    [Fact]
    public async Task Party_municipalities_come_from_address_and_city()
    {
        var h = new Harness();
        h.ServeNote(OutgoingNote);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        GoodsInvoice invoice = (await h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026"))).Document;

        Assert.Equal("3550308", invoice.Issuer.MunicipalityCode);
        Assert.Equal("3304557", invoice.Recipient.MunicipalityCode);
        Assert.Equal(new Address { Street = "Av. das Nações Unidas", Number = "12901", District = "Brooklin", PostalCode = "04795100" }, invoice.Issuer.Address);
        Assert.Equal(new Address { Street = "Estrada do Galeão", Number = "135", District = "Ilha do Governador", PostalCode = "21931385" }, invoice.Recipient.Address);
    }

    // ---------- entrada inválida e throttling ----------

    [Fact]
    public async Task Invalid_settings_and_invalid_locator_fail_without_the_network()
    {
        var h = new Harness("""{"url":""}""");
        await Assert.ThrowsAsync<ConnectorSettingsException>(() => h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026")));

        var g = new Harness();
        await Assert.ThrowsAsync<FormatException>(() => g.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026") with { Locator = "d365/brmf/BRMF21-10000026" }));

        Assert.Empty(h.Http.Requests);
        Assert.Empty(g.Http.Requests);
    }

    [Fact]
    public void Locator_check_uses_the_d365_format_without_the_network()
    {
        var h = new Harness();

        Assert.Null(h.Source.CheckLocator(Reference(OutgoingNote, "BRMF21-10000026")));
        string? problem = h.Source.CheckLocator(Reference(OutgoingNote, "BRMF21-10000026") with { Locator = "nfe/tenant-a/x.xml" });

        Assert.NotNull(problem);
        Assert.Contains("d365/<dataAreaId>/<FiscalDocumentRecId>", problem);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Short_retry_after_is_honoured_and_the_assembly_goes_on()
    {
        var h = new Harness();
        h.Http.Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(2)));
        h.ServeNote(OutgoingNote);
        h.ServeReference("postaladdress-22565428565", "city-22565694955", "postaladdress-22565441071", "city-22565694958");

        FetchResult<GoodsInvoice> result = await h.Source.FetchAsync(Reference(OutgoingNote, "BRMF21-10000026"));

        Assert.Equal([TimeSpan.FromSeconds(2)], h.Time.Delays);
        Assert.Equal(3, result.Document.Items.Count);
    }

    [Fact]
    public void Reports_the_dynamics365_origin()
    {
        Assert.Equal("Dynamics365", new Harness().Source.Origin);
    }

    // ---------- apoio ----------

    private static DocumentReference Reference(long recId, string voucher) => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = $"brmf|{voucher}",
        Locator = $"d365/brmf/{recId}",
        Origin = "Dynamics365",
    };

    private static NameValueCollection Query(HttpRequestMessage request) => HttpUtility.ParseQueryString(request.RequestUri!.Query);

    private static string EntitySet(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.Split('/').Last();

    private sealed class Harness
    {
        public Harness(string? settings = null)
        {
            Profiles.Profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = "Dynamics365",
                InboundSettings = settings ?? $$"""{"url":"{{Env}}","companies":["brmf"]}""",
                OutboundAdapter = "Avalara",
            };
            Source = new D365GoodsInvoiceSource(
                new HttpClient(Http), Profiles, new D365ChangeFeedTests.FakeTokens(), new D365ChangeFeedOptions(),
                new D365ReferenceDataCache(new D365AssemblyOptions(), Time), Trace, Time, NullLogger<D365GoodsInvoiceSource>.Instance);
        }

        public SequencedHttpMessageHandler Http { get; } = new();
        public D365ChangeFeedTests.FakeProfiles Profiles { get; } = new();
        public D365ChangeFeedTests.FakeTime Time { get; } = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        public RecordingTrace Trace { get; } = new();
        public D365GoodsInvoiceSource Source { get; }

        /// <summary>Enfileira as 4 respostas gravadas da nota (e a contábil do voucher, se pedida).</summary>
        public void ServeNote(long recId, bool withAccounting = false)
        {
            foreach (string file in new[] { "header", "lines", "taxes", "charges" })
            {
                Http.Respond(Text(D365Fixtures.Note(recId, file)));
            }

            if (withAccounting)
            {
                Http.Respond(Text(D365Fixtures.Note(recId, "taxtrans")));
            }
        }

        public void ServeReference(params string[] files)
        {
            foreach (string file in files)
            {
                Http.Respond(Text($"reference/{file}.json"));
            }
        }
    }

    internal sealed class RecordingTrace : IProcessingTrace
    {
        public string? SourceKey { get; private set; }
        public string? SourceContent { get; private set; }
        public string? SourceFormat { get; private set; }

        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default)
        {
            (SourceKey, SourceContent, SourceFormat) = (naturalKey, content, format);
            return Task.CompletedTask;
        }

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;
    }
}
