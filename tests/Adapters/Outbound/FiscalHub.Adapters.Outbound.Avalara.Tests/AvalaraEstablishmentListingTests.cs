using System.Net;
using System.Text.Json.Nodes;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica a listagem de estabelecimentos da Avalara (spec avalara-establishment-listing, design D6 e D7): as empresas
/// num array puro e os contribuintes de cada empresa em <c>{"value": [...]}</c>, com o <c>$select</c> e o
/// <c>$orderby</c>; a paginação pelo cliente, com o <c>$skip</c> pelos itens recebidos e a parada só na página vazia; e o
/// tudo ou nada — qualquer falha deixa a listagem inteira sem uso. A plataforma é falsa e conta as requisições.
/// </summary>
public class AvalaraEstablishmentListingTests
{
    // --- A leitura (2.3) ---

    [Fact]
    public async Task Both_formats_give_the_normalized_cnpj_and_both_codes()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        IReadOnlyList<PlatformEstablishment> listed = await h.Listing().ListAsync(Profile());

        PlatformEstablishment matriz = Assert.Single(listed, e => e.TaxId == "44278225000180");
        Assert.Equal(("012", "010", "10001"), (matriz.CompanyCode, matriz.EstablishmentCode, matriz.PlatformId));
        Assert.Equal(["44278225000180", "44278225000260", "44278225000341", "11222333000181"], listed.Select(e => e.TaxId));
    }

    [Fact]
    public async Task A_company_with_several_taxpayers_and_a_company_without_any_are_both_read()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        IReadOnlyList<PlatformEstablishment> listed = await h.Listing().ListAsync(Profile());

        Assert.Equal(3, listed.Count(e => e.CompanyCode == "012"));   // a 012 com três estabelecimentos
        Assert.DoesNotContain(listed, e => e.CompanyCode == "Comércio");   // a Comércio veio com {"value": []}
        Assert.Equal(3, h.Platform.To(PlatformHandler.TaxpayersPath).Select(r => r.Query["empresaId"]).Distinct().Count());
    }

    [Fact]
    public async Task Codes_are_text_as_they_came()
    {
        var platform = new PlatformHandler()
            .WithCompany(1, "005", "zeros", ("11111111000111", "001", 1))
            .WithCompany(2, "Padrão", "acento", ("22222222000122", " 002 ", 2))   // sem Trim: o espaço é do código
            .WithCompany(3, "QA", "número", ("33333333000133", null, 3));
        platform.Taxpayers["3"][0]["codigo"] = 1;   // número JSON: ausente, sem virar "1"
        var h = new Harness(platform);

        IReadOnlyList<PlatformEstablishment> listed = await h.Listing().ListAsync(Profile());

        Assert.Equal("005", listed[0].CompanyCode);
        Assert.Equal(("Padrão", " 002 "), (listed[1].CompanyCode, listed[1].EstablishmentCode));
        Assert.Null(listed[2].EstablishmentCode);
    }

    [Fact]
    public async Task A_numeric_company_code_is_missing_and_never_converted()
    {
        var platform = new PlatformHandler().WithCompany(8120, null, "sem texto", ("44278225000180", "010", 10001));
        platform.Companies[0]["codigoCIA"] = 5;
        var h = new Harness(platform);

        IReadOnlyList<PlatformEstablishment> listed = await h.Listing().ListAsync(Profile());

        Assert.Null(Assert.Single(listed).CompanyCode);
    }

    [Theory]
    [InlineData(8120, "8120")]
    [InlineData("A-8120", "A-8120")]
    public async Task The_company_id_goes_in_the_query_as_it_came(object empresaId, string expected)
    {
        var h = new Harness(new PlatformHandler().WithCompany(empresaId, "005", "x", ("44278225000180", "010", 10001)));

        await h.Listing().ListAsync(Profile());

        Assert.All(h.Platform.To(PlatformHandler.TaxpayersPath), r => Assert.Equal(expected, r.Query["empresaId"]));
    }

    [Fact]
    public async Task Every_page_carries_the_select_and_the_orderby_and_never_the_subscription()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        await h.Listing(new AvalaraOptions { ListingPageSize = 2 }).ListAsync(Profile());

        Assert.All(h.Platform.To(PlatformHandler.CompaniesPath), r =>
        {
            Assert.Equal("empresaId,codigoCIA,descricao", r.Query["$select"]);
            Assert.Equal("empresaId", r.Query["$orderby"]);
            Assert.Equal("2", r.Query["$top"]);
            Assert.Null(r.Query["subscriptionId"]);
        });
        Assert.All(h.Platform.To(PlatformHandler.TaxpayersPath), r =>
        {
            Assert.Equal("contribuinteId,codigo,cnpj", r.Query["$select"]);
            Assert.Equal("contribuinteId", r.Query["$orderby"]);
            Assert.Equal("2", r.Query["$top"]);
            Assert.Null(r.Query["subscriptionId"]);
        });
    }

    [Fact]
    public async Task Fields_beyond_the_select_are_ignored()
    {
        var platform = new PlatformHandler();
        platform.Companies.AddRange(JsonNode.Parse(PlatformHandler.Fixture("empresas-campos-a-mais.json"))!.AsArray().Select(n => n!.AsObject()));
        platform.Taxpayers["8120"] = [.. JsonNode.Parse(PlatformHandler.Fixture("contribuintes-8120-campos-a-mais.json"))!["value"]!.AsArray()
            .Select(n => n!.AsObject())];
        var h = new Harness(platform);

        PlatformEstablishment only = Assert.Single(await h.Listing().ListAsync(Profile()));

        Assert.Equal(new PlatformEstablishment("44278225000180", "012", "010", "10001", "METALURGICA EXEMPLO (fixture)"), only);
    }

    [Fact]
    public async Task The_description_reaches_the_record_of_the_company()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        IReadOnlyList<PlatformEstablishment> listed = await h.Listing().ListAsync(Profile());

        Assert.All(listed.Where(e => e.CompanyCode == "012"), e => Assert.Equal("METALURGICA EXEMPLO (fixture)", e.CompanyName));
    }

    [Fact]
    public async Task Each_list_costs_its_pages_and_the_empty_one_that_confirms_the_end()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        await h.Listing().ListAsync(Profile());

        Assert.Equal(2, h.Platform.Count(PlatformHandler.CompaniesPath));    // a página e a vazia
        Assert.Equal(5, h.Platform.Count(PlatformHandler.TaxpayersPath));    // a Comércio vazia já na primeira: 2 + 1 + 2
        Assert.Equal(
            ["8120", "8120", "8121", "8122", "8122"],
            h.Platform.To(PlatformHandler.TaxpayersPath).Select(r => r.Query["empresaId"]));   // em sequência, empresa a empresa
    }

    [Fact]
    public async Task Three_companies_each_with_taxpayers_in_one_page_are_two_and_six_requests()
    {
        var h = new Harness(new PlatformHandler()
            .WithCompany(1, "A", "a", ("11111111000111", "1", 1))
            .WithCompany(2, "B", "b", ("22222222000122", "2", 2))
            .WithCompany(3, "C", "c", ("33333333000133", "3", 3)));

        await h.Listing().ListAsync(Profile());

        Assert.Equal(2, h.Platform.Count(PlatformHandler.CompaniesPath));
        Assert.Equal(6, h.Platform.Count(PlatformHandler.TaxpayersPath));
    }

    [Fact]
    public async Task Every_request_carries_the_bearer_of_the_active_section()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        await h.Listing().ListAsync(Profile(environment: "Production"));

        Assert.All(h.Platform.Requests, r => Assert.Equal("Bearer tok-prod", r.Authorization));
        Assert.All(h.Platform.Requests, r => Assert.Equal("avalara-a-prod", r.Uri.Host));   // a URL base da seção ativa
        Assert.All(h.Tokens.Asked, s => Assert.Equal("production", s.Environment));
    }

    [Fact]
    public async Task Listing_and_submit_share_one_token_request()
    {
        var endpoint = new AvalaraTokenProviderTests.TokenEndpointStub();
        var secrets = new AvalaraTokenProviderTests.FakeSecrets();
        secrets.Values["fh-tenant-a--outbound--sandbox--clientsecret"] = "segredo-a";
        var provider = new AvalaraTokenProvider(
            new HttpClient(endpoint), secrets, Options.Create(new AvalaraOptions()), new AvalaraTokenProviderTests.FakeClock(),
            new CapturingLogger<AvalaraTokenProvider>());
        var platform = PlatformHandler.FromFixtures();
        var listing = new AvalaraEstablishmentListing(
            new HttpClient(platform), provider, Options.Create(new AvalaraOptions()), new CapturingLogger<AvalaraEstablishmentListing>());

        await listing.ListAsync(Profile());
        AvalaraAccessToken submitToken = await provider.GetTokenAsync(AvalaraOutboundSettings.Read("tenant-a", Profile()));   // o que o envio pede

        Assert.Equal(1, endpoint.Calls);
        Assert.All(platform.Requests, r => Assert.Equal($"Bearer {submitToken.Value}", r.Authorization));
    }

    [Fact]
    public async Task A_failure_in_the_third_company_throws_and_returns_nothing()
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Query["empresaId"] == "8122" ? PlatformHandler.Json(HttpStatusCode.ServiceUnavailable, "{}") : null;
        var h = new Harness(platform);

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Listing().ListAsync(Profile()));
    }

    // --- A paginação (2.4) ---

    [Fact]
    public async Task With_top_two_three_companies_take_three_pages_and_the_short_page_does_not_stop()
    {
        var h = new Harness(new PlatformHandler()
            .WithCompany(1, "A", "a")
            .WithCompany(2, "B", "b")
            .WithCompany(3, "C", "c"));

        await h.Listing(new AvalaraOptions { ListingPageSize = 2 }).ListAsync(Profile());

        Assert.Equal([0, 2, 3], h.Platform.To(PlatformHandler.CompaniesPath).Select(r => r.Skip));
        Assert.Equal(["1", "2", "3"], h.Platform.To(PlatformHandler.TaxpayersPath).Select(r => r.Query["empresaId"]));
    }

    [Fact]
    public async Task A_server_page_limit_below_the_top_loses_nothing()
    {
        // Pedidos 100, recebidos 10: com o $skip somando o $top, os itens de 10 a 24 nunca seriam lidos.
        (string?, string?, object?)[] taxpayers = [.. Enumerable.Range(1, 25).Select(i => ((string?)$"{i:D8}000100", (string?)$"{i:D3}", (object?)i))];
        var platform = new PlatformHandler { ServerPageLimit = 10 }.WithCompany(8120, "012", "x", taxpayers);
        var h = new Harness(platform);

        IReadOnlyList<PlatformEstablishment> listed = await h.Listing().ListAsync(Profile());

        Assert.Equal(25, listed.Count);
        Assert.Equal([0, 10, 20, 25], h.Platform.To(PlatformHandler.TaxpayersPath).Select(r => r.Skip));
    }

    [Fact]
    public async Task The_next_company_starts_at_skip_zero()
    {
        var h = new Harness(new PlatformHandler()
            .WithCompany(1, "A", "a", ("11111111000111", "1", 1), ("11111111000112", "2", 2), ("11111111000113", "3", 3))
            .WithCompany(2, "B", "b", ("22222222000122", "1", 4)));

        await h.Listing(new AvalaraOptions { ListingPageSize = 2 }).ListAsync(Profile());

        Assert.Equal(
            [("1", 0), ("1", 2), ("1", 3), ("2", 0), ("2", 1)],
            h.Platform.To(PlatformHandler.TaxpayersPath).Select(r => (r.Query["empresaId"], r.Skip)));
    }

    [Fact]
    public async Task A_failure_in_the_second_page_throws_and_returns_not_even_the_first()
    {
        var platform = new PlatformHandler().WithCompany(8120, "012", "x",
            ("11111111000111", "1", 1), ("11111111000112", "2", 2), ("11111111000113", "3", 3));
        platform.Override = r => r.Path == PlatformHandler.TaxpayersPath && r.Skip == 2
            ? PlatformHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
            : null;
        var h = new Harness(platform);

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Listing(new AvalaraOptions { ListingPageSize = 2 }).ListAsync(Profile()));
    }

    [Fact]
    public async Task An_ignored_skip_is_refused_naming_the_endpoint_and_the_skip()
    {
        var h = new Harness(new PlatformHandler { IgnoreSkip = true }.WithCompany(1, "A", "a").WithCompany(2, "B", "b").WithCompany(3, "C", "c"));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(
            () => h.Listing(new AvalaraOptions { ListingPageSize = 2 }).ListAsync(Profile()));

        Assert.StartsWith("Contrato do destino: ", ex.Reason);
        Assert.Contains("taxcompliance/v2/empresa", ex.Reason);
        Assert.Contains("$skip=2", ex.Reason);
        Assert.Equal([0, 2], h.Platform.To(PlatformHandler.CompaniesPath).Select(r => r.Skip));   // nenhuma página a mais
    }

    [Fact]
    public async Task The_page_ceiling_is_refused_naming_the_endpoint_the_company_and_the_ceiling()
    {
        // Páginas diferentes que não acabam: o $orderby e o $skip ignorados juntos. Só o teto as pega.
        var platform = new PlatformHandler().WithCompany(8120, "012", "x");
        int n = 0;
        platform.Override = r => r.Path == PlatformHandler.TaxpayersPath
            ? PlatformHandler.Json(HttpStatusCode.OK, $$"""{"value":[{"contribuinteId":{{++n}},"codigo":"x","cnpj":"1"}]}""")
            : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(
            () => h.Listing(new AvalaraOptions { ListingMaxPages = 3 }).ListAsync(Profile()));

        Assert.StartsWith("Contrato do destino: ", ex.Reason);
        Assert.Contains("taxcompliance/v2/contribuinte", ex.Reason);
        Assert.Contains("8120", ex.Reason);
        Assert.Contains("3 páginas", ex.Reason);
        Assert.Equal(3, h.Platform.Count(PlatformHandler.TaxpayersPath));   // a 4ª não é pedida
    }

    [Fact]
    public void The_default_top_is_one_hundred_and_the_ceiling_fifty()
    {
        var options = new AvalaraOptions();

        Assert.Equal(100, options.ListingPageSize);
        Assert.Equal(50, options.ListingMaxPages);
        Assert.Equal("taxcompliance/v2/empresa", options.CompaniesPath);
        Assert.Equal("taxcompliance/v2/contribuinte", options.TaxpayersPath);
    }

    [Fact]
    public async Task The_default_top_goes_in_every_page()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        await h.Listing().ListAsync(Profile());

        Assert.All(h.Platform.Requests, r => Assert.Equal("100", r.Query["$top"]));
    }

    // --- As falhas (2.5) ---

    [Fact]
    public async Task Companies_in_an_envelope_are_a_contract_refusal()
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.CompaniesPath ? PlatformHandler.Json(HttpStatusCode.OK, """{"value":[]}""") : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.StartsWith("Contrato do destino: ", ex.Reason);
        Assert.Contains("taxcompliance/v2/empresa", ex.Reason);
    }

    [Theory]
    [InlineData("""[{"contribuinteId":1}]""")]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"value":{}}""")]
    [InlineData("não é json")]
    public async Task Taxpayers_without_the_value_array_are_a_contract_refusal(string body)
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.TaxpayersPath ? PlatformHandler.Json(HttpStatusCode.OK, body) : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.StartsWith("Contrato do destino: ", ex.Reason);
        Assert.Contains("taxcompliance/v2/contribuinte", ex.Reason);
    }

    [Fact]
    public async Task A_company_without_id_is_a_contract_refusal()
    {
        // Sem o empresaId, os contribuintes dela não podem ser lidos: seguir sem ela seria a listagem parcial.
        var platform = PlatformHandler.FromFixtures();
        platform.Companies[1].Remove("empresaId");
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.StartsWith("Contrato do destino: ", ex.Reason);
        Assert.Contains("empresaId", ex.Reason);
    }

    [Fact]
    public async Task A_401_with_a_cached_token_invalidates_it_and_is_transient()
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.TaxpayersPath ? PlatformHandler.Json(HttpStatusCode.Unauthorized, "{}") : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => h.Listing().ListAsync(Profile()));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(h.Tokens.Invalidated);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task A_denied_listing_is_a_connector_refusal_pointing_to_the_table(HttpStatusCode status, bool freshToken)
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.CompaniesPath
            ? PlatformHandler.Json(status, """{"mensagens":["sem permissão para listar empresas"]}""")
            : null;
        var h = new Harness(platform) { FreshToken = freshToken };

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.StartsWith("Configuração do conector: a plataforma negou a listagem", ex.Reason);
        Assert.Contains("tenant 'tenant-a'", ex.Reason);
        Assert.Contains("ambiente 'sandbox'", ex.Reason);
        Assert.Contains($"HTTP {(int)status}", ex.Reason);
        Assert.Contains("sem permissão para listar empresas", ex.Reason);
        Assert.Contains("OutboundSettings.sandbox.establishments", ex.Reason);
        Assert.Empty(h.Tokens.Invalidated);
    }

    [Theory]
    [InlineData(PlatformHandler.CompaniesPath, "taxcompliance/v2/empresa", "Avalara:CompaniesPath")]
    [InlineData(PlatformHandler.TaxpayersPath, "taxcompliance/v2/contribuinte", "Avalara:TaxpayersPath")]
    public async Task A_404_points_to_both_parts_of_the_url(string path, string relative, string option)
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == path ? new HttpResponseMessage(HttpStatusCode.NotFound) : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.StartsWith("Configuração do conector: o caminho da listagem", ex.Reason);
        Assert.Contains("HTTP 404 em GET https://avalara-a/", ex.Reason);
        Assert.Contains("OutboundSettings.sandbox.baseUrl", ex.Reason);
        Assert.Contains($"'{relative}', em {option}", ex.Reason);
        Assert.DoesNotContain("$top", ex.Reason);   // a URL sem a query
    }

    [Fact]
    public async Task Another_4xx_is_a_refusal_with_the_status()
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.TaxpayersPath
            ? PlatformHandler.Json(HttpStatusCode.BadRequest, """{"mensagens":["campo de ordenação inválido: contribuinteId"]}""")
            : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.Contains("HTTP 400", ex.Reason);
        Assert.Contains("campo de ordenação inválido: contribuinteId", ex.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Unavailability_is_transient(HttpStatusCode status)
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.CompaniesPath ? PlatformHandler.Json(status, "{}") : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => h.Listing().ListAsync(Profile()));

        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task A_network_failure_is_transient()
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = _ => throw new HttpRequestException("o host não respondeu");
        var h = new Harness(platform);

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Listing().ListAsync(Profile()));
    }

    [Fact]
    public async Task The_body_is_redacted_before_the_reason()
    {
        var platform = PlatformHandler.FromFixtures();
        platform.Override = r => r.Path == PlatformHandler.CompaniesPath
            ? PlatformHandler.Json(HttpStatusCode.Forbidden, """{"mensagens":["token tok-a recusado"]}""")
            : null;
        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Listing().ListAsync(Profile()));

        Assert.DoesNotContain("tok-a", ex.Reason);
    }

    [Fact]
    public async Task The_log_has_counts_and_pages_but_no_content()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        await h.Listing().ListAsync(Profile());

        string log = h.Logger.All;
        Assert.Contains("3 empresas", log);
        Assert.Contains("4 contribuintes", log);
        Assert.DoesNotContain("44278225000180", log);
        Assert.DoesNotContain("METALURGICA", log);
    }

    [Fact]
    public void The_adapter_name_is_the_one_of_the_credential_test()
    {
        var h = new Harness(PlatformHandler.FromFixtures());

        Assert.Equal(new AvalaraCredentialTest(h.Tokens).Adapter, h.Listing().Adapter);
    }

    private static TenantConnectorProfile Profile(string environment = "Sandbox") => new()
    {
        TenantId = "tenant-a",
        Environment = environment,
        InboundAdapter = "Dynamics365",
        OutboundAdapter = "Avalara",
        OutboundSettings = """
            {"sandbox":{"baseUrl":"https://avalara-a/","clientId":"id-a","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret"},
             "production":{"baseUrl":"https://avalara-a-prod/","clientId":"id-a","clientSecretRef":"kv:fh-tenant-a--outbound--production--clientsecret"}}
            """,
    };

    private sealed class Harness(PlatformHandler platform)
    {
        public PlatformHandler Platform { get; } = platform;

        public bool FreshToken { get; init; }

        public ListingTokens Tokens => _tokens ??= new ListingTokens(FreshToken);

        public CapturingLogger<AvalaraEstablishmentListing> Logger { get; } = new();

        private ListingTokens? _tokens;

        public AvalaraEstablishmentListing Listing(AvalaraOptions? options = null)
            => new(new HttpClient(Platform), Tokens, Options.Create(options ?? new AvalaraOptions()), Logger);
    }

    /// <summary>Token falso por ambiente: guarda as seções pedidas e os tokens invalidados.</summary>
    private sealed class ListingTokens(bool fresh) : IAvalaraTokenProvider
    {
        public List<AvalaraOutboundSettings> Asked { get; } = [];

        public List<AvalaraAccessToken> Invalidated { get; } = [];

        public Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
        {
            Asked.Add(settings);
            string value = settings.Environment == "production" ? "tok-prod" : "tok-a";
            return Task.FromResult(new AvalaraAccessToken(settings.TenantId, settings.Environment, value, fresh, key: null));
        }

        public void Invalidate(AvalaraAccessToken token) => Invalidated.Add(token);

        public void Forget(string tenantId)
        {
        }

        public Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
