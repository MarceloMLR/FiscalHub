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
    private const string Matriz = "44278225000180";   // a empresa da brmf, como o diretório a dá: o CNPJ completo da matriz
    private const string Sp01 = "44278225000260";

    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    // ---------- a mesma referência do coletor (D6) ----------

    [Fact]
    public async Task Same_header_gives_the_same_reference_through_the_feed_and_the_discovery()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));
        IReadOnlyList<DocumentReference> discovered = (await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"))).References;

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

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"))).References;

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

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria(Sp01, "SP-01", start: new DateOnly(2026, 8, 8), end: new DateOnly(2026, 8, 7)));

        Assert.Empty(result.References);
        Assert.Null(result.EstablishmentTaxId);   // o pedido inválido não resolve escopo
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Company_not_in_the_register_is_empty_after_only_the_register_read()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register));

        Assert.Empty((await h.Discovery.DiscoverAsync(Criteria("12345678000190", "0001"))).References);
        Assert.Single(h.Http.Requests);
    }

    [Fact]
    public async Task Document_number_narrows_the_read()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));

        await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01") with { DocumentNumber = "125" });

        Assert.EndsWith(" and FiscalDocumentNumber eq '125'", Query(h.Http.Requests[1])["$filter"]);
    }

    // ---------- a empresa pela raiz (change company-root-in-directory, revista em 2026-10-05) ----------
    // A empresa chega pelo CNPJ completo da matriz, como o diretório a dá e o agendamento a grava; a comparação é pela raiz.

    [Fact]
    public async Task The_company_without_branch_scopes_the_four_establishments()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond("""{"value":[]}""");

        await h.Discovery.DiscoverAsync(Criteria(Matriz, branch: null));

        string filter = Query(h.Http.Requests[1])["$filter"]!;
        foreach (string code in new[] { "Matriz", "SP-01", "SAL-01", "RJ-01" })
        {
            Assert.Contains($"(dataAreaId eq 'brmf' and FiscalEstablishment eq '{code}')", filter);
        }

        Assert.Equal(4, filter.Split("dataAreaId eq").Length - 1);
    }

    [Fact]
    public async Task Without_branch_every_establishment_of_the_root_enters_even_with_a_repeated_cnpj()
    {
        // Derivada: o cadastro gravado, com um segundo estabelecimento no CNPJ da SP-01.
        JsonObject register = JsonNode.Parse(D365Fixtures.Text(Register))!.AsObject();
        register["value"]!.AsArray().Add(new JsonObject
        {
            ["dataAreaId"] = "brmf", ["FiscalEstablishmentId"] = "SP-02", ["CNPJ"] = "442782250002-60", ["Name"] = "Filial de serviços 2",
        });
        var h = new Harness();
        h.Http.Respond(register.ToJsonString()).Respond(D365Fixtures.Text(SpPeriod));

        await h.Discovery.DiscoverAsync(Criteria(Matriz, branch: null));

        string filter = Query(h.Http.Requests[1])["$filter"]!;
        Assert.Contains("(dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-02')", filter);
        Assert.Equal(5, filter.Split("dataAreaId eq").Length - 1);
    }

    [Fact]
    public async Task The_whole_company_brings_notes_of_more_than_one_establishment()
    {
        // Derivada: as duas NFS-e gravadas da SP-01, e uma terceira, igual à primeira, lançada na Matriz. As notas gravadas da
        // Matriz e da SAL-01 nos períodos de directory/ são modelo 01, fora do mapa, e o mapeamento as tiraria antes da
        // guarda.
        JsonObject period = JsonNode.Parse(D365Fixtures.Text(SpPeriod))!.AsObject();
        JsonObject matriz = period["value"]![0]!.DeepClone().AsObject();
        matriz["FiscalEstablishment"] = "Matriz";
        matriz["FiscalEstablishmentCNPJCPF"] = "442782250001-80";
        matriz["Voucher"] = "BRMF06-110000099";
        matriz["FiscalDocumentRecId"] = 68719470000L;
        period["value"]!.AsArray().Insert(0, matriz);

        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(period.ToJsonString());

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(Criteria(Matriz, branch: null))).References;

        Assert.Equal(["Matriz", "SP-01", "SP-01"], found.Select(r => r.Metadata!.BranchCode));
        Assert.Equal(["44278225000180", "44278225000260", "44278225000260"], found.Select(r => r.Metadata!.CompanyCode));   // o CNPJ do estabelecimento, como antes
    }

    [Fact]
    public async Task The_company_with_a_branch_scopes_only_that_branch()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"))).References;

        Assert.EndsWith("and ((dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-01'))", Query(h.Http.Requests[1])["$filter"]);
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task The_recorded_schedule_of_the_matriz_scopes_only_the_matriz()
    {
        // O único agendamento gravado no banco de dev (2026-10-05): a empresa 44278225000180 com a filial Matriz.
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond("""{"value":[]}""");

        await h.Discovery.DiscoverAsync(Criteria(Matriz, "Matriz"));

        Assert.EndsWith("and ((dataAreaId eq 'brmf' and FiscalEstablishment eq 'Matriz'))", Query(h.Http.Requests[1])["$filter"]);
    }

    [Fact]
    public async Task The_cnpj_of_a_branch_without_branch_scopes_the_whole_company()
    {
        // A comparação é pela raiz: um CNPJ de filial, sem filial, é a empresa inteira, e não mais só aquele estabelecimento.
        // No banco de dev não há agendamento assim (conferido em 2026-10-05).
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond("""{"value":[]}""");

        await h.Discovery.DiscoverAsync(Criteria(Sp01, branch: null));

        Assert.Equal(4, Query(h.Http.Requests[1])["$filter"]!.Split("dataAreaId eq").Length - 1);
    }

    [Fact]
    public async Task A_note_whose_cnpj_is_of_another_root_stays_out_with_a_log()
    {
        // Derivada: a segunda NFS-e gravada da SP-01, com um CNPJ de outra raiz no estabelecimento próprio.
        JsonObject period = JsonNode.Parse(D365Fixtures.Text(SpPeriod))!.AsObject();
        period["value"]![1]!["FiscalEstablishmentCNPJCPF"] = "112223330001-81";
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(period.ToJsonString());

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"))).References;

        Assert.Equal(["brmf|BRMF06-110000034"], found.Select(r => r.NaturalKey));
        Assert.Contains(h.Logger.Entries, e => e.Text.Contains("brmf|BRMF06-110000035") && e.Text.Contains("não é da empresa 44278225000180"));
    }

    [Fact]
    public async Task A_note_with_another_cnpj_of_the_same_root_enters()
    {
        // Derivada: a segunda NFS-e gravada, com outra ordem da mesma raiz no cabeçalho. Antes da change, a igualdade do CNPJ
        // a tirava; pela raiz, ela é da empresa pedida.
        JsonObject period = JsonNode.Parse(D365Fixtures.Text(SpPeriod))!.AsObject();
        period["value"]![1]!["FiscalEstablishmentCNPJCPF"] = "442782250099-99";
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(period.ToJsonString());

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"))).References;

        Assert.Equal(["brmf|BRMF06-110000034", "brmf|BRMF06-110000035"], found.Select(r => r.NaturalKey));
    }

    [Fact]
    public async Task The_alphanumeric_company_finds_its_establishment()
    {
        // Derivada: o cadastro gravado, com um estabelecimento de CNPJ alfanumérico.
        JsonObject register = JsonNode.Parse(D365Fixtures.Text(Register))!.AsObject();
        register["value"]!.AsArray().Add(new JsonObject
        {
            ["dataAreaId"] = "brmf", ["FiscalEstablishmentId"] = "ALFA-01", ["CNPJ"] = "12.ABC.345/01DE-35", ["Name"] = "Filial alfanumérica",
        });
        var h = new Harness();
        h.Http.Respond(register.ToJsonString()).Respond("""{"value":[]}""");

        await h.Discovery.DiscoverAsync(Criteria("12ABC34501DE35", branch: null));

        Assert.EndsWith("and ((dataAreaId eq 'brmf' and FiscalEstablishment eq 'ALFA-01'))", Query(h.Http.Requests[1])["$filter"]);
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

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"))).References;

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

        IReadOnlyList<DocumentReference> found = (await h.Discovery.DiscoverAsync(
            Criteria(Matriz, "Matriz", start: new DateOnly(2017, 1, 15), end: new DateOnly(2017, 1, 15)))).References;

        Assert.Empty(found);
        Assert.Contains(h.Logger.Warnings, w => w.Contains("'01'") && w.Contains("BRMF06-110000030"));
    }

    // ---------- o estabelecimento que a descoberta resolveu (change explicit-credential-and-execution-cnpj, D5) ----------
    // O CNPJ é o do cadastro, decidido pelo escopo antes de ler as notas: vale com zero notas, e não depende do que as notas
    // trazem. Sem CNPJ quando o escopo tem mais de um estabelecimento, ou nenhum.

    [Fact]
    public async Task The_sp01_gives_its_establishment_cnpj()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"));

        Assert.Equal(2, result.References.Count);
        Assert.Equal(Sp01, result.EstablishmentTaxId);
    }

    [Fact]
    public async Task The_sp01_without_any_note_still_gives_its_establishment_cnpj()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond("""{"value":[]}""");

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"));

        Assert.Empty(result.References);
        Assert.Equal(Sp01, result.EstablishmentTaxId);
    }

    [Fact]
    public async Task The_whole_company_with_four_establishments_gives_no_cnpj()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(D365Fixtures.Text(SpPeriod));

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria(Matriz, branch: null));

        Assert.Equal(2, result.References.Count);
        Assert.Null(result.EstablishmentTaxId);
    }

    [Fact]
    public async Task A_root_with_a_single_establishment_asked_without_branch_gives_its_cnpj()
    {
        // Derivada: o cadastro gravado, com um estabelecimento de outra raiz, sozinho nela.
        JsonObject register = JsonNode.Parse(D365Fixtures.Text(Register))!.AsObject();
        register["value"]!.AsArray().Add(new JsonObject
        {
            ["dataAreaId"] = "brmf", ["FiscalEstablishmentId"] = "OUT-01", ["CNPJ"] = "11.222.333/0001-81", ["Name"] = "Outra empresa",
        });
        var h = new Harness();
        h.Http.Respond(register.ToJsonString()).Respond("""{"value":[]}""");

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria("11222333000181", branch: null));

        Assert.Equal("11222333000181", result.EstablishmentTaxId);
    }

    [Fact]
    public async Task The_alphanumeric_establishment_gives_its_cnpj_with_the_letters()
    {
        // Derivada: o cadastro gravado, com um estabelecimento de CNPJ alfanumérico.
        JsonObject register = JsonNode.Parse(D365Fixtures.Text(Register))!.AsObject();
        register["value"]!.AsArray().Add(new JsonObject
        {
            ["dataAreaId"] = "brmf", ["FiscalEstablishmentId"] = "ALFA-01", ["CNPJ"] = "12.ABC.345/01DE-35", ["Name"] = "Filial alfanumérica",
        });
        var h = new Harness();
        h.Http.Respond(register.ToJsonString()).Respond("""{"value":[]}""");

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria("12ABC34501DE35", "ALFA-01"));

        Assert.Equal("12ABC34501DE35", result.EstablishmentTaxId);
    }

    [Fact]
    public async Task A_branch_outside_the_register_gives_no_cnpj_and_reads_no_notes()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register));

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria(Matriz, "XX-99"));

        Assert.Empty(result.References);
        Assert.Null(result.EstablishmentTaxId);
        Assert.Single(h.Http.Requests);   // só o cadastro
    }

    [Fact]
    public async Task A_note_with_another_cnpj_of_the_same_root_does_not_change_the_establishment_cnpj()
    {
        // Derivada: a segunda NFS-e gravada da SP-01 traz outra ordem da mesma raiz. O CNPJ é o do cadastro, e não o da nota.
        JsonObject period = JsonNode.Parse(D365Fixtures.Text(SpPeriod))!.AsObject();
        period["value"]![1]!["FiscalEstablishmentCNPJCPF"] = "442782250099-99";
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond(period.ToJsonString());

        DiscoveryResult result = await h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01"));

        Assert.Equal(["44278225000260", "44278225009999"], result.References.Select(r => r.Metadata!.CompanyCode));
        Assert.Equal(Sp01, result.EstablishmentTaxId);
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

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01")));

        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Profile_without_auth_fails_with_the_reason_and_without_any_request()
    {
        // O provider de produção, e não o falso: a falta de credencial é o motivo, e nenhuma outra identidade entra
        // (change explicit-credential-and-execution-cnpj, D2).
        var http = new SequencedHttpMessageHandler();
        var profiles = new FakeProfiles
        {
            Profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                InboundAdapter = "Dynamics365",
                InboundSettings = $$"""{"url":"{{Env}}","companies":["brmf"]}""",
                OutboundAdapter = "Avalara",
            },
        };
        var discovery = new D365DocumentDiscovery(
            new HttpClient(http), profiles,
            new ClientCredentialsD365TokenProvider(new EmptyVault(), new ListLogger<ClientCredentialsD365TokenProvider>()),
            new D365ChangeFeedOptions(), new FakeTime(DateTimeOffset.UnixEpoch), new ListLogger<D365DocumentDiscovery>());

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(() => discovery.DiscoverAsync(Criteria(Matriz, "SP-01")));

        Assert.StartsWith("A credencial do ERP não está configurada", ex.Message);
        Assert.Empty(http.Requests);   // nem o cadastro, nem as notas
    }

    [Fact]
    public async Task Forbidden_on_the_notes_names_the_privilege_of_the_header_entity()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Register)).Respond("""{"error":{"message":"denied"}}""", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Discovery.DiscoverAsync(Criteria(Matriz, "SP-01")));

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
