using System.Collections.Specialized;
using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;
using static FiscalHub.Adapters.Ingress.D365Poll.Tests.D365ChangeFeedTests;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica a descoberta por período do D365 (spec period-discovery, design D4 e D6) sobre as respostas GRAVADAS do
/// fiscosysdev (<c>Fixtures/d365/directory/</c>): o cadastro da <c>brmf</c> e a consulta da <c>SP-01</c> em 2026-08-07, com
/// as duas NFS-e daquele dia. Onde a base não tem o caso, o teste diz "derivada". HTTP e token falsos, sem rede.
/// </summary>
public class D365DocumentDiscoveryTests
{
    private const string Env = "https://fiscosysdev.operations.dynamics.com";
    private const string Register = "directory/establishments.json";
    private const string SpPeriod = "directory/period-SP-01-2026-08-07.json";
    private const string Sp01 = "44278225000260";

    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    // ---------- a mesma referência do coletor (D6) ----------

    [Fact]
    public async Task Same_header_gives_the_same_reference_through_the_feed_and_the_discovery()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));
        IReadOnlyList<DocumentReference> discovered = await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01"));

        var feedHttp = new SequencedHttpMessageHandler().Respond(D365Fixtures.Text(SpPeriod));
        var feed = new D365ChangeFeed(new HttpClient(feedHttp), h.Profiles, new FakeTokens(), new D365ChangeFeedOptions(), new FakeTime(DateTimeOffset.UnixEpoch), new ListLogger<D365ChangeFeed>());
        var fromFeed = new List<DocumentReference>();
        await foreach (ChangeFeedPage page in feed.PullAsync("tenant-a", DateTimeOffset.UnixEpoch))
        {
            fromFeed.AddRange(page.Items.Select(i => i.Reference));
        }

        Assert.Equal(2, discovered.Count);
        Assert.Equal(fromFeed.Select(r => (r.NaturalKey, r.Locator, r.Type, r.Metadata)), discovered.Select(r => (r.NaturalKey, r.Locator, r.Type, r.Metadata)));
    }

    // ---------- a consulta ----------

    [Fact]
    public async Task Reads_the_fiscal_days_of_the_brt_bounds_for_the_establishment_ordered_by_rec_id()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));

        IReadOnlyList<DocumentReference> found = await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01"));

        Assert.Equal(2, h.Http.Requests.Count);
        Assert.Equal($"{Env}/data/FiscalEstablishments", h.Http.Requests[0].RequestUri!.GetLeftPart(UriPartial.Path));
        HttpRequestMessage documents = h.Http.Requests[1];
        Assert.Equal($"{Env}/data/FSFiscalDocumentBRs", documents.RequestUri!.GetLeftPart(UriPartial.Path));
        NameValueCollection q = Query(documents);
        Assert.Equal("true", q["cross-company"]);
        // O fim às 23:59:59 de Brasília é 02:59:59Z do dia seguinte: o dia é o da ponta, sem conversão.
        Assert.Equal(
            "FiscalDocumentDate ge 2026-08-07T00:00:00Z and FiscalDocumentDate le 2026-08-07T23:59:59Z and ((dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-01'))",
            q["$filter"]);
        Assert.Equal("FiscalDocumentRecId", q["$orderby"]);
        Assert.Equal("500", q["$top"]);
        Assert.Equal(D365HeaderReference.Select.Split(','), q["$select"]!.Split(','));

        Assert.Equal(["brmf|BRMF06-110000034", "brmf|BRMF06-110000035"], found.Select(r => r.NaturalKey));
        Assert.Equal("d365/brmf/68719477966", found[0].Locator);
        Assert.All(found, r =>
        {
            Assert.Equal("Dynamics365", r.Origin);
            Assert.Equal(DocumentType.ServiceNfse, r.Type);   // todos os modelos entram; o roteamento ignora a NFS-e
            Assert.Equal(Sp01, r.Metadata!.CompanyCode);
            Assert.Equal("SP-01", r.Metadata.BranchCode);
            Assert.Equal(new DateOnly(2026, 8, 7), r.Metadata.ReferenceDate);
        });
    }

    [Fact]
    public async Task End_before_start_is_empty_without_any_request()
    {
        var h = new Harness();

        IReadOnlyList<DocumentReference> found = await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01", start: new DateOnly(2026, 8, 8), end: new DateOnly(2026, 8, 7)));

        Assert.Empty(found);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Company_not_in_the_register_is_empty_after_only_the_register_read()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register));

        Assert.Empty(await h.Discovery.DiscoverAsync(Criteria("12345678", "0001")));
        Assert.Single(h.Http.Requests);
    }

    [Fact]
    public async Task Without_branch_every_establishment_of_the_cnpj_enters()
    {
        // Derivada: o cadastro gravado, com um segundo estabelecimento no CNPJ da SP-01.
        JsonObject register = JsonNode.Parse(D365Fixtures.Text(Register))!.AsObject();
        register["value"]!.AsArray().Add(new JsonObject
        {
            ["dataAreaId"] = "brmf", ["FiscalEstablishmentId"] = "SP-02", ["CNPJ"] = "442782250002-60", ["Name"] = "Filial de serviços 2",
        });
        var h = new Harness();
        h.Http.Respond(register.ToJsonString()).Respond(D365Fixtures.Text(SpPeriod));

        await h.Discovery.DiscoverAsync(Criteria(Sp01, branch: null));

        Assert.EndsWith(
            "((dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-01') or (dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-02'))",
            Query(h.Http.Requests[1])["$filter"]);
    }

    [Fact]
    public async Task Document_number_narrows_the_read()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));

        await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01") with { DocumentNumber = "125" });

        Assert.EndsWith(" and FiscalDocumentNumber eq '125'", Query(h.Http.Requests[1])["$filter"]);
    }

    [Fact]
    public async Task Note_of_the_establishment_with_another_cnpj_stays_out()
    {
        // Derivada: a segunda NFS-e gravada, com o CNPJ antigo de um estabelecimento que mudou de CNPJ.
        JsonObject period = JsonNode.Parse(D365Fixtures.Text(SpPeriod))!.AsObject();
        period["value"]![1]!["FiscalEstablishmentCNPJCPF"] = "442782250099-99";
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(period.ToJsonString());

        IReadOnlyList<DocumentReference> found = await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01"));

        Assert.Equal(["brmf|BRMF06-110000034"], found.Select(r => r.NaturalKey));
    }

    [Fact]
    public async Task Reads_page_by_page_by_rec_id_keyset_until_the_short_page()
    {
        // Derivada: as duas NFS-e gravadas, uma por página, com o pageSize 1 do perfil.
        JsonNode[] rows = [.. JsonNode.Parse(D365Fixtures.Text(SpPeriod))!["value"]!.AsArray().Select(r => r!.DeepClone())];
        var h = new Harness($$"""{"url":"{{Env}}","companies":["brmf"],"pageSize":1}""");
        h.Http
            .Respond(D365Fixtures.Text(Register))
            .Respond(D365Fixtures.Response(rows[0]))
            .Respond(D365Fixtures.Response(rows[1]))
            .Respond("""{"value":[]}""");

        IReadOnlyList<DocumentReference> found = await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01"));

        Assert.Equal(["brmf|BRMF06-110000034", "brmf|BRMF06-110000035"], found.Select(r => r.NaturalKey));
        Assert.Equal(4, h.Http.Requests.Count);
        Assert.DoesNotContain("FiscalDocumentRecId gt", Query(h.Http.Requests[1])["$filter"]);
        Assert.EndsWith(" and FiscalDocumentRecId gt 68719477966", Query(h.Http.Requests[2])["$filter"]);
        Assert.EndsWith(" and FiscalDocumentRecId gt 68719478716", Query(h.Http.Requests[3])["$filter"]);
    }

    [Fact]
    public async Task Model_outside_the_map_warns_and_does_not_stop_the_read()
    {
        // Gravada: a nota da Matriz em 2017-01-15 é de modelo 01, fora do mapa padrão (55, 57, SE).
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text("directory/period-Matriz-2017-01-15.json"));

        IReadOnlyList<DocumentReference> found = await h.Discovery.DiscoverAsync(
            Criteria("44278225000180", "Matriz", start: new DateOnly(2017, 1, 15), end: new DateOnly(2017, 1, 15)));

        Assert.Empty(found);
        Assert.Contains(h.Logger.Warnings, w => w.Contains("'01'") && w.Contains("BRMF06-110000030"));
    }

    // ---------- a busca por chave (reprocesso) ----------

    [Theory]
    [InlineData("35260612345678000190550010000001231000000123")]   // a chave de acesso de um XML
    [InlineData("|BRMF06-110000034")]
    [InlineData("brmf|")]
    public async Task Key_that_is_not_company_and_voucher_is_null_without_any_request(string key)
    {
        var h = new Harness();

        Assert.Null(await h.Discovery.FindByKeyAsync("tenant-a", key));
        Assert.Empty(h.Http.Requests);
        Assert.Empty(h.Tokens.Connections);
    }

    [Fact]
    public async Task Key_finds_the_note_by_company_and_voucher()
    {
        JsonNode row = JsonNode.Parse(D365Fixtures.Text(SpPeriod))!["value"]![0]!;
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Response(row));

        DocumentReference? reference = await h.Discovery.FindByKeyAsync("tenant-a", "brmf|BRMF06-110000034");

        NameValueCollection q = Query(h.Http.Requests.Single());
        Assert.Equal("dataAreaId eq 'brmf' and Voucher eq 'BRMF06-110000034'", q["$filter"]);
        Assert.Equal("2", q["$top"]);
        Assert.Equal("d365/brmf/68719477966", reference!.Locator);
        Assert.Equal("Dynamics365", reference.Origin);
    }

    [Fact]
    public async Task Key_without_the_note_in_the_origin_is_null()
    {
        var h = new Harness();
        h.Http.Respond("""{"value":[]}""");

        Assert.Null(await h.Discovery.FindByKeyAsync("tenant-a", "brmf|BRMF99-0"));
    }

    [Fact]
    public async Task Key_with_two_notes_fails_naming_the_key()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(SpPeriod));   // duas linhas

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Discovery.FindByKeyAsync("tenant-a", "brmf|BRMF06-110000034"));

        Assert.Contains("brmf|BRMF06-110000034", ex.Message);
    }

    // ---------- as falhas ----------

    [Fact]
    public async Task Invalid_settings_fail_without_any_request()
    {
        var h = new Harness("""{"url":""}""");

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01")));

        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Forbidden_on_the_notes_names_the_privilege_of_the_header_entity()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond("""{"error":{"message":"denied"}}""", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01")));

        Assert.Contains("FSFiscalDocumentBRView", ex.Message);
        Assert.Contains("HTTP 403", ex.Message);
    }

    // ---------- apoio ----------

    private static NameValueCollection Query(HttpRequestMessage request) => HttpUtility.ParseQueryString(request.RequestUri!.Query);

    /// <summary>O critério como a tela e o agendador o mandam: o dia cheio em Brasília.</summary>
    private static DiscoveryCriteria Criteria(string company, string? branch, DateOnly? start = null, DateOnly? end = null) => new()
    {
        TenantId = "tenant-a",
        Start = new DateTimeOffset((start ?? new DateOnly(2026, 8, 7)).ToDateTime(TimeOnly.MinValue), Brt),
        End = new DateTimeOffset((end ?? new DateOnly(2026, 8, 7)).ToDateTime(new TimeOnly(23, 59, 59)), Brt),
        Company = company,
        Establishment = branch,
    };

    private sealed class Harness
    {
        public Harness(string? settings = null)
        {
            Profiles.Profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                InboundAdapter = "Dynamics365",
                InboundSettings = settings ?? $$"""{"url":"{{Env}}","companies":["brmf"]}""",
                OutboundAdapter = "Avalara",
            };
            Discovery = new D365DocumentDiscovery(new HttpClient(Http), Profiles, Tokens, new D365ChangeFeedOptions(), new FakeTime(DateTimeOffset.UnixEpoch), Logger);
        }

        public SequencedHttpMessageHandler Http { get; } = new();
        public FakeProfiles Profiles { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public ListLogger<D365DocumentDiscovery> Logger { get; } = new();
        public D365DocumentDiscovery Discovery { get; }
    }
}
