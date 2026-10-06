using System.Net;
using System.Text.Json.Nodes;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica os códigos da empresa pelo dispatcher (specs avalara-document-contract, platform-establishment-resolution e
/// compliance-dispatch-outcome; design D3, D5 e D9): a tabela <c>establishments</c> ganha, e a plataforma completa, pelo
/// resolvedor real sobre a listagem real da Avalara. Recusa onde haveria escolha ou invenção, nunca escolhe, e lista uma
/// vez por janela — nunca uma por nota. A plataforma é falsa, e conta as requisições.
/// </summary>
public class AvalaraDispatcherPlatformCodesTests
{
    private const string Issuer = "12345678000190";      // o emitente da nota de exemplo
    private const string Recipient = "98765432000110";   // o destinatário

    [Fact]
    public async Task The_entry_wins_with_no_listing_request()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "010", 10001)));

        await h.Dispatcher(Entry(Issuer, "MANUAL-E", "MANUAL-C")).SubmitAsync(Own(), Context());

        Assert.Equal(("MANUAL-E", "MANUAL-C"), h.SubmittedCodes());
        Assert.Equal(0, h.ListingRequests);
    }

    [Fact]
    public async Task An_incomplete_entry_does_not_fall_back_to_the_platform()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "010", 10001)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(
            () => h.Dispatcher($$$"""{"{{{Issuer}}}":{"codigoEmpresa":"MANUAL-E"}}""").SubmitAsync(Own(), Context()));

        Assert.Contains("codigoContribuinte", ex.Reason);
        Assert.Equal(0, h.Posts);
        Assert.Equal(0, h.ListingRequests);
    }

    [Fact]
    public async Task Without_an_entry_the_codes_come_from_the_platform_and_neither_is_the_cnpj()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "010", 10001)));

        await h.Dispatcher().SubmitAsync(Own(), Context());

        Assert.Equal(("012", "010"), h.SubmittedCodes());
        Assert.NotEqual(Issuer, h.SubmittedCodes().Empresa);
    }

    [Fact]
    public async Task Without_a_taxpayer_the_refusal_names_the_cnpj_and_nothing_is_posted()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", ("44278225000180", "010", 10001)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Equal(
            $"Configuração do conector: o estabelecimento {Issuer} não tem contribuinte cadastrado na plataforma (tenant 'tenant-a', "
            + "ambiente 'sandbox'). Cadastre-o na plataforma, ou traduza-o em OutboundSettings.sandbox.establishments. Se o cadastro "
            + "acabou de ser feito, salve o perfil do conector (Configurações → Conectores) para o hub reler a plataforma, e "
            + "reprocesse a nota.",
            ex.Reason);
        Assert.Equal(0, h.Posts);
    }

    [Fact]
    public async Task A_duplicate_is_refused_naming_both_candidates_without_the_company_id()
    {
        var h = new Harness(new PlatformHandler()
            .WithCompany(8120, "012", "METALURGICA EXEMPLO...", (Issuer, "001", 10001))
            .WithCompany(8122, "009", "LABORATORIO", (Issuer, "001", 20001)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Equal(
            $"Configuração do conector: o estabelecimento {Issuer} tem 2 contribuintes na plataforma (tenant 'tenant-a', ambiente "
            + "'sandbox'), e o hub não escolhe entre eles: empresa '012' (METALURGICA EXEMPLO...), contribuinte '001' (#10001); "
            + "empresa '009' (LABORATORIO), contribuinte '001' (#20001). Remova a duplicidade na plataforma, ou traduza o estabelecimento em "
            + "OutboundSettings.sandbox.establishments.",
            ex.Reason);
        Assert.DoesNotContain("8120", ex.Reason);   // o empresaId não acrescenta nada legível
        Assert.Equal(0, h.Posts);
    }

    [Fact]
    public async Task Two_taxpayers_with_the_same_code_in_the_same_company_are_told_apart_by_the_id()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "001", 10001), (Issuer, "001", 10002)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Contains("contribuinte '001' (#10001); empresa '012' (METALURGICA), contribuinte '001' (#10002)", ex.Reason);
    }

    [Fact]
    public async Task A_missing_description_or_id_is_left_out_of_the_candidate()
    {
        var h = new Harness(new PlatformHandler()
            .WithCompany(8120, "012", null, (Issuer, "001", null))
            .WithCompany(8122, "009", "LABORATORIO", (Issuer, null, 20001)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Contains("empresa '012', contribuinte '001'; empresa '009' (LABORATORIO), contribuinte sem código (#20001).", ex.Reason);
    }

    [Fact]
    public async Task Seven_candidates_name_five_and_say_how_many_are_left()
    {
        var platform = new PlatformHandler();
        for (int i = 1; i <= 7; i++)
        {
            platform.WithCompany(8200 + i, $"E{i}", $"Empresa {i}", (Issuer, "001", 10000 + i));
        }

        var h = new Harness(platform);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Contains("tem 7 contribuintes", ex.Reason);
        Assert.Contains("(#10005); e mais 2.", ex.Reason);
        Assert.DoesNotContain("#10006", ex.Reason);
    }

    [Theory]
    [InlineData("012", null, "o código do contribuinte (codigo)")]
    [InlineData(null, "010", "o código da empresa (codigoCIA)")]
    [InlineData(null, null, "o código da empresa (codigoCIA) e o código do contribuinte (codigo)")]
    public async Task The_only_taxpayer_without_a_code_is_refused_naming_the_field(string? company, string? code, string missing)
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, company, "METALURGICA", (Issuer, code, 10001)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Contains($"o único contribuinte do estabelecimento {Issuer} na plataforma", ex.Reason);
        Assert.Contains($"está sem {missing}", ex.Reason);
        Assert.Contains("(METALURGICA)", ex.Reason);   // a empresa
        Assert.Equal(0, h.Posts);
    }

    [Fact]
    public async Task A_resolver_without_the_listing_gives_the_missing_translation_of_always()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "010", 10001)), withListing: false);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Equal(
            $"Configuração do conector: o tenant 'tenant-a', no ambiente 'sandbox', não tem tradução para o estabelecimento {Issuer} "
            + "(faltam codigoEmpresa e codigoContribuinte em OutboundSettings.sandbox.establishments).",
            ex.Reason);
        Assert.Empty(h.Platform.Requests);
    }

    [Fact]
    public async Task Without_issuance_only_the_platform_knowing_the_issuer_makes_it_ours()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "010", 10001)));

        await h.Dispatcher().SubmitAsync(AvalaraComplianceDispatcherTests.SampleInvoice(), Context());

        Assert.Equal(("012", "010"), h.SubmittedCodes());
        Assert.Equal(Recipient, (string?)h.Submitted()["parceiro"]!["cnpj"]);   // o destinatário é o parceiro
    }

    [Fact]
    public async Task Without_issuance_the_issuer_in_the_table_and_the_recipient_on_the_platform_is_refused_citing_both()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Recipient, "020", 10002)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(
            () => h.Dispatcher(Entry(Issuer, "E", "C")).SubmitAsync(AvalaraComplianceDispatcherTests.SampleInvoice(), Context()));

        Assert.Contains($"emitente {Issuer}", ex.Reason);
        Assert.Contains($"destinatário {Recipient}", ex.Reason);
        Assert.Contains("pela tradução em OutboundSettings.sandbox.establishments ou pela plataforma", ex.Reason);
        Assert.Equal(0, h.Posts);
    }

    [Fact]
    public async Task Without_issuance_and_no_side_of_the_tenant_is_refused_citing_both()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", ("44278225000180", "010", 10001)));

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(
            () => h.Dispatcher().SubmitAsync(AvalaraComplianceDispatcherTests.SampleInvoice(), Context()));

        Assert.Contains($"emitente {Issuer}", ex.Reason);
        Assert.Contains($"destinatário {Recipient}", ex.Reason);
        Assert.Contains("nem contribuinte na plataforma", ex.Reason);
    }

    [Fact]
    public async Task A_d365_note_with_the_table_makes_no_listing_request()
    {
        var h = new Harness(new PlatformHandler().WithCompany(8120, "012", "METALURGICA", (Issuer, "010", 10001)));

        await h.Dispatcher(Entry(Issuer, "E", "C")).SubmitAsync(Own(), Context());
        await h.Dispatcher(Entry(Issuer, "E", "C")).SubmitAsync(AvalaraComplianceDispatcherTests.SampleInvoice() with { Issuance = Issuance.ThirdParty, Recipient = new Party { TaxId = Issuer, Name = "Nós" } }, Context());

        Assert.Equal(0, h.ListingRequests);
        Assert.Equal(2, h.Posts);
    }

    [Fact]
    public async Task N_submissions_of_the_same_establishment_list_once()
    {
        var h = new Harness(new PlatformHandler()
            .WithCompany(1, "A", "a", (Issuer, "010", 1))
            .WithCompany(2, "B", "b", ("22222222000122", "2", 2))
            .WithCompany(3, "C", "c", ("33333333000133", "3", 3)));
        AvalaraComplianceDispatcher dispatcher = h.Dispatcher();

        for (int i = 0; i < 20; i++)
        {
            await dispatcher.SubmitAsync(Own(), Context());
        }

        Assert.Equal(2, h.Platform.Count(PlatformHandler.CompaniesPath));
        Assert.Equal(6, h.Platform.Count(PlatformHandler.TaxpayersPath));
        Assert.Equal(20, h.Posts);
    }

    [Fact]
    public async Task A_refused_listing_is_a_rejection_and_an_unavailable_one_is_not()
    {
        var refused = new Harness(PlatformHandler.FromFixtures());
        refused.Platform.Override = r => r.Path == PlatformHandler.CompaniesPath ? PlatformHandler.Json(HttpStatusCode.Forbidden, "{}") : null;
        var unavailable = new Harness(PlatformHandler.FromFixtures());
        unavailable.Platform.Override = r => r.Path == PlatformHandler.CompaniesPath ? PlatformHandler.Json(HttpStatusCode.ServiceUnavailable, "{}") : null;

        await Assert.ThrowsAsync<DispatchRejectedException>(() => refused.Dispatcher().SubmitAsync(Own(), Context()));
        var transient = await Assert.ThrowsAsync<HttpRequestException>(() => unavailable.Dispatcher().SubmitAsync(Own(), Context()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, transient.StatusCode);
        Assert.Equal(0, refused.Posts + unavailable.Posts);
    }

    private static GoodsInvoice Own() => AvalaraComplianceDispatcherTests.SampleInvoice() with { Issuance = Issuance.Own };

    private static string Entry(string cnpj, string empresa, string contribuinte)
        => $$$"""{"{{{cnpj}}}":{"codigoEmpresa":"{{{empresa}}}","codigoContribuinte":"{{{contribuinte}}}"}}""";

    private static DispatchContext Context() => new()
    {
        TenantId = "tenant-a",
        NaturalKey = "nfe-1",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private sealed class Harness
    {
        private readonly Tokens _tokens = new();
        private readonly PlatformEstablishmentResolver _resolver;

        public Harness(PlatformHandler platform, bool withListing = true)
        {
            Platform = platform;
            IPlatformEstablishmentListing[] listings = withListing
                ? [new AvalaraEstablishmentListing(new HttpClient(platform), _tokens, Options.Create(new AvalaraOptions()), new CapturingLogger<AvalaraEstablishmentListing>())]
                : [];
            _resolver = new PlatformEstablishmentResolver(listings, new PlatformEstablishmentOptions(), TimeProvider.System);
        }

        public PlatformHandler Platform { get; }

        public int ListingRequests => Platform.Count(PlatformHandler.CompaniesPath) + Platform.Count(PlatformHandler.TaxpayersPath);

        public int Posts => Platform.Requests.Count(r => r.Method == HttpMethod.Post);

        /// <summary>Um dispatcher sobre o mesmo resolvedor: a janela é do processo, e não do dispatcher.</summary>
        public AvalaraComplianceDispatcher Dispatcher(string? establishments = null)
        {
            string table = establishments is null ? string.Empty : $",\"establishments\":{establishments}";
            var profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                InboundAdapter = "Dynamics365",
                OutboundAdapter = "Avalara",
                OutboundSettings = $$$"""
                    {"sandbox":{"baseUrl":"https://avalara-a/","clientId":"id-a","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret"{{{table}}}}}
                    """,
            };
            return new AvalaraComplianceDispatcher(
                new HttpClient(Platform), Options.Create(new AvalaraOptions()), _tokens, new NoOpProcessingTrace(), new OneProfile(profile),
                _resolver, new CapturingLogger<AvalaraComplianceDispatcher>(), TimeProvider.System);
        }

        public JsonNode Submitted() => JsonNode.Parse(Platform.SubmittedBodies.Last())!;

        public (string? Empresa, string? Contribuinte) SubmittedCodes()
            => ((string?)Submitted()["codigoEmpresa"], (string?)Submitted()["codigoContribuinte"]);
    }

    private sealed class OneProfile(TenantConnectorProfile profile) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default) => Task.FromResult<TenantConnectorProfile?>(profile);

        public Task UpsertAsync(TenantConnectorProfile p, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Tokens : IAvalaraTokenProvider
    {
        public Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
            => Task.FromResult(new AvalaraAccessToken(settings.TenantId, settings.Environment, "tok-a", isFresh: false, key: null));

        public void Invalidate(AvalaraAccessToken token)
        {
        }

        public void Forget(string tenantId)
        {
        }

        public Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
