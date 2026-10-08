using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// As respostas reais do sandbox (design D15, change <c>connect-avalara-sandbox</c>): nenhuma fixture carrega credencial ou
/// token, e cada uma reexercita o adapter contra a resposta de verdade. No envio, só a recusa aconteceu (2026-09-27); o
/// aceite, a consulta de status e a recusa de credencial pelo hub não foram exercitados, e não se fabrica resposta.
/// <para>
/// A listagem (change <c>platform-listing-shape</c>, D5): as três respostas do <c>/empresa</c> com a query, de 2026-10-06,
/// curadas à mão. A varredura impõe a curadoria — nenhum CNPJ, e todo valor fora da lista do que fica mascarado —, e a
/// reprodução lê a primeira página e a vazia reais, e confere a evidência do <c>$skip</c>.
/// </para>
/// </summary>
public partial class SandboxFixtureTests
{
    // O motivo que a PlatformMessage tira da recusa real: o resumo do mapa "errors" do ProblemDetails, sem o "title"
    // (establishment-and-readable-dashboard, D7). A lista inteira, campo a campo, fica na foto, de onde a tela a lê.
    private const string RealRefusalReason = "6 campos com erro: operacao, tipoPagamento, parceiro.Codigo e mais 3";

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sandbox");

    // O que fica real numa resposta gravada da listagem: a forma e o que o hub lê. O resto sai como "[mascarado]".
    private static readonly HashSet<string> KeptInListing = ["empresaId", "codigoCIA", "contribuinteId", "codigo"];

    // ---------- 18.2: varredura ----------

    [Fact]
    public void No_fixture_carries_a_credential_or_a_token()
    {
        string[] files = Directory.GetFiles(FixtureDir, "*", SearchOption.AllDirectories);
        Assert.Contains(files, f => f.EndsWith(".json", StringComparison.Ordinal));   // a varredura não passa no vazio

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            Assert.False(BearerWithValue().IsMatch(text), $"{name}: Bearer com valor");
            Assert.False(Jwt().IsMatch(text), $"{name}: JWT");
            Assert.False(CredentialWithValue().IsMatch(text), $"{name}: access_token ou client_secret com valor");
        }
    }

    // A curadoria das respostas da listagem é à mão (change platform-listing-shape, D5), então a regra fica aqui, e não em
    // quem curou: nenhum CNPJ, e todo valor de item fora da lista do que fica mascarado.
    [Fact]
    public void No_listing_fixture_carries_a_cnpj_or_a_value_outside_what_stays()
    {
        string[] files = Directory.GetFiles(FixtureDir, "listagem-*.json");
        Assert.NotEmpty(files);   // a varredura não passa no vazio

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            Assert.False(Cnpj().IsMatch(text), $"{name}: CNPJ");
            Assert.Empty(Unmasked(JsonNode.Parse(text)!["response"]!["body"]!).Select(property => $"{name}: {property} sem máscara"));
        }
    }

    [Theory]
    [InlineData("""{"cnpj":"11222333000181"}""", true)]
    [InlineData("""{"cnpj":"11.222.333/0001-81"}""", true)]
    [InlineData("""{"cnpj":"[mascarado]","empresaId":8120,"contribuinteId":2000010001}""", false)]
    public void The_cnpj_scan_catches_both_spellings(string sample, bool caught) => Assert.Equal(caught, Cnpj().IsMatch(sample));

    [Theory]
    [InlineData("""{"value":[{"empresaId":8120,"codigoCIA":"012","descricao":"EMPRESA DE VERDADE"}]}""", "descricao")]
    [InlineData("""[{"contribuinteId":2000010001,"codigo":"010","razao":"CONTRIBUINTE DE VERDADE"}]""", "razao")]
    [InlineData("""{"value":[{"empresaId":8120,"codigoCIA":"012","descricao":"[mascarado]","idPortalCompany":"[mascarado]"}]}""", null)]
    [InlineData("""{"value":[]}""", null)]
    public void The_masking_scan_catches_a_value_outside_what_stays(string body, string? caught)
    {
        string[] expected = caught is null ? [] : [caught];

        Assert.Equal(expected, Unmasked(JsonNode.Parse(body)!));
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def-ghi", true)]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJh", true)]
    [InlineData("{\"access_token\":\"xyz\"}", true)]
    [InlineData("{\"client_secret\": \"s3cr3t\"}", true)]
    [InlineData("Bearer [redigido]", false)]
    [InlineData("{\"access_token\":\"[redigido]\",\"token_type\":\"bearer\"}", false)]
    public void The_scan_catches_what_it_looks_for(string sample, bool caught)
        => Assert.Equal(caught, BearerWithValue().IsMatch(sample) || Jwt().IsMatch(sample) || CredentialWithValue().IsMatch(sample));

    // ---------- 18.3: reprodução ----------

    [Fact]
    public void Platform_message_on_the_real_refusal_is_readable_text()
    {
        (int status, string body) = RecordedSubmit("recusa-no-envio.json");

        string reason = PlatformMessage.Extract(body, status);

        Assert.Equal(RealRefusalReason, reason);
        Assert.DoesNotContain("{", reason);          // texto, e não o JSON cru
        Assert.DoesNotContain("traceId", reason);
        Assert.DoesNotContain("One or more validation errors occurred.", reason);   // o title do ProblemDetails não vaza
    }

    [Fact]
    public async Task Real_refusal_is_a_rejection_with_its_reason_after_a_single_post_and_the_photo_recorded()
    {
        (int status, string body) = RecordedSubmit("recusa-no-envio.json");
        var handler = new RecordedHandler(status, body);
        var trace = new PhotoTrace();
        var dispatcher = new AvalaraComplianceDispatcher(
            new HttpClient(handler), Options.Create(new AvalaraOptions { DocumentsPath = "taxcompliance/v2/fiscal/dfe" }),
            new NoOpAvalaraTokenProvider(), trace, new OneProfile(),
            new PlatformEstablishmentResolver([], new PlatformEstablishmentOptions(), TimeProvider.System),
            new CapturingLogger<AvalaraComplianceDispatcher>(), TimeProvider.System);

        DispatchRejectedException ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => dispatcher.SubmitAsync(Invoice(), Context()));

        Assert.Equal($"Plataforma de compliance recusou: {RealRefusalReason}", ex.Reason);
        Assert.Equal(1, handler.Requests);

        // A foto: sem o ruído do ProblemDetails, com o que prova o ambiente e abre chamado na plataforma (D10).
        JsonNode photo = JsonNode.Parse(trace.Submit!)!;
        Assert.Equal(400, (int?)photo["response"]!["status"]);
        Assert.Equal("POST", (string?)photo["request"]!["method"]);
        Assert.Equal("https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/fiscal/dfe", (string?)photo["request"]!["url"]);
        JsonObject photoBody = photo["response"]!["body"]!.AsObject();
        Assert.False(photoBody.ContainsKey("type"));
        Assert.False(photoBody.ContainsKey("title"));
        Assert.False(photoBody.ContainsKey("status"));
        Assert.Equal("00-bb582c93ed4d2544429b0af1cbf7fd05-c04a22f297974e99-00", (string?)photoBody["traceId"]);
        Assert.Equal(6, photoBody["errors"]!.AsObject().Count);
    }

    // ---------- a listagem (change platform-listing-shape, D5) ----------

    [Fact]
    public async Task The_real_page_and_the_real_empty_page_end_the_reading_with_the_five_companies()
    {
        // A primeira página e a vazia, como o sandbox as devolveu com a query: as duas em envelope. O adapter de antes desta
        // change recusava já a primeira. A vazia veio com $skip=999, e aqui encerra a leitura no $skip=5: a forma é a dela.
        (_, string firstPage) = RecordedSubmit("listagem-empresas-top5.json");
        (_, string emptyPage) = RecordedSubmit("listagem-empresas-vazia.json");
        var platform = new PlatformHandler
        {
            Override = r => r.Path == PlatformHandler.CompaniesPath ? PlatformHandler.Json(HttpStatusCode.OK, r.Skip == 0 ? firstPage : emptyPage) : null,
        };
        var logger = new CapturingLogger<AvalaraEstablishmentListing>();
        var listing = new AvalaraEstablishmentListing(
            new HttpClient(platform), new NoOpAvalaraTokenProvider(), Options.Create(new AvalaraOptions { ListingPageSize = 5 }), logger);

        await listing.ListAsync(ListingProfile());

        Assert.Equal([0, 5], platform.To(PlatformHandler.CompaniesPath).Select(r => r.Skip));
        Assert.Equal(RecordedIds("listagem-empresas-top5.json"), platform.To(PlatformHandler.TaxpayersPath).Select(r => r.Query["empresaId"]!).Distinct());
        Assert.Contains("5 empresas em 2 páginas", logger.All);
    }

    [Fact]
    public void The_recorded_skip_brings_the_third_and_the_fourth_of_the_first_page()
    {
        // A evidência do $skip e da ordem estável, conferível no repositório, e não só no relato. Os identificadores vêm dos
        // arquivos, e não do teste: um empresaId real só existe no Fixtures/sandbox/.
        Assert.Equal(RecordedIds("listagem-empresas-top5.json").Skip(2).Take(2), RecordedIds("listagem-empresas-skip2.json"));
    }

    private static List<string> RecordedIds(string file)
        => [.. JsonNode.Parse(RecordedSubmit(file).Body)!["value"]!.AsArray().Select(i => i!["empresaId"]!.ToJsonString())];

    private static TenantConnectorProfile ListingProfile() => new()
    {
        TenantId = "tenant-a",
        Environment = "Sandbox",
        InboundAdapter = "Dynamics365",
        OutboundAdapter = "Avalara",
        OutboundSettings = """{"sandbox":{"baseUrl":"https://api-gateway.sandbox.avalarabrasil.com.br/"}}""",
    };

    // O status e o corpo gravados no envelope, como a plataforma devolveu (já redigidos).
    internal static (int Status, string Body) RecordedSubmit(string file)
    {
        JsonNode envelope = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir, file)))!;
        return ((int)envelope["response"]!["status"]!, envelope["response"]!["body"]!.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    // Os valores de item da listagem fora da lista do que fica que não estão mascarados: só o nome da propriedade.
    private static List<string> Unmasked(JsonNode body)
    {
        JsonArray items = body as JsonArray ?? body["value"]?.AsArray() ?? [];
        return [.. items.Select(i => i!.AsObject())
            .SelectMany(item => item)
            .Where(p => !KeptInListing.Contains(p.Key) && !(p.Value is JsonValue v && v.TryGetValue(out string? text) && text == "[mascarado]"))
            .Select(p => p.Key)];
    }

    [GeneratedRegex(@"(?<!\d)\d{14}(?!\d)|\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}")]
    private static partial Regex Cnpj();

    [GeneratedRegex(@"Bearer\s+(?!\[redigido\])[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerWithValue();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"""(access_token|client_secret)""\s*:\s*""(?!\[redigido\])[^""]+""", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialWithValue();

    private static DispatchContext Context() => new()
    {
        TenantId = "tenant-a",
        NaturalKey = "brmf|BRMF12-30000001",
        CorrelationId = "corr-1",
        Operation = DocumentStatus.Issued,
    };

    private static GoodsInvoice Invoice() => new()
    {
        AccessKey = "",
        Model = "55",
        Series = "1",
        Number = "1",
        IssueDate = new DateTimeOffset(2016, 9, 2, 12, 0, 0, TimeSpan.Zero),
        Issuance = Issuance.Own,
        Issuer = new Party { TaxId = "44278225000180", Name = "Contoso Entertainment System Brazil" },
        Recipient = new Party { TaxId = "98765432000110", Name = "Destinatário" },
        TotalAmount = 10m,
        Items =
        [
            new GoodsInvoiceItem
            {
                Number = 1, ProductCode = "BRMF710", Description = "Item", Ncm = "12345678", Cfop = "5102",
                Quantity = 1m, UnitAmount = 10m, TotalAmount = 10m, Unit = "pcs",
            },
        ],
    };

    private sealed class RecordedHandler(int status, string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
            response.Headers.Date = DateTimeOffset.UtcNow;   // o único cabeçalho que o sandbox trouxe na lista
            return Task.FromResult(response);
        }
    }

    private sealed class OneProfile : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantConnectorProfile?>(new TenantConnectorProfile
            {
                TenantId = tenantId,
                Environment = "Sandbox",
                InboundAdapter = "Dynamics365",
                OutboundAdapter = "Avalara",
                OutboundSettings = """{"sandbox":{"baseUrl":"https://api-gateway.sandbox.avalarabrasil.com.br","establishments":{"44278225000180":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"}}}}""",
            });

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }

    private sealed class PhotoTrace : IProcessingTrace
    {
        public string? Submit { get; private set; }

        public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default)
        {
            if (exchange == TraceExchanges.Submit)
            {
                Submit = json;
            }

            return Task.CompletedTask;
        }
    }
}
