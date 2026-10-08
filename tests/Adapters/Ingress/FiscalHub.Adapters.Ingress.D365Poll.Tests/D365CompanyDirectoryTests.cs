using System.Collections.Specialized;
using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using Azure.Identity;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using FiscalHub.Application.Inbound;
using static FiscalHub.Adapters.Ingress.D365Poll.Tests.D365ChangeFeedTests;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica o diretório de empresas do D365 (spec company-directory, design D1 e D2) sobre o cadastro GRAVADO da
/// <c>brmf</c> (<c>Fixtures/d365/directory/establishments.json</c>): a empresa é identificada pela raiz do CNPJ e mostrada
/// pelo CNPJ completo da matriz (change company-root-in-directory), e a filial é o código do estabelecimento, com o CNPJ
/// dele. As filiais de uma empresa são as dos estabelecimentos da mesma raiz. A lista vem do cadastro, e não das notas: o <c>RJ-01</c> não tem nenhuma. A falha vira um motivo seguro,
/// sem token nem cabeçalho. HTTP e token falsos, sem rede.
/// </summary>
public class D365CompanyDirectoryTests
{
    private const string Env = "https://fiscosysdev.operations.dynamics.com";
    private const string Brmf = "directory/establishments.json";

    // ---------- a consulta ----------

    [Fact]
    public async Task Reads_the_standard_entity_across_companies_with_the_four_fields()
    {
        var h = new Harness($$"""{"url":"{{Env}}"}""");
        h.Http.Respond(D365Fixtures.Text(Brmf));

        await h.Directory.ListCompaniesAsync("tenant-a");

        HttpRequestMessage request = Assert.Single(h.Http.Requests);
        Assert.Equal($"{Env}/data/FiscalEstablishments", request.RequestUri!.GetLeftPart(UriPartial.Path));
        NameValueCollection q = Query(request);
        Assert.Equal("true", q["cross-company"]);
        Assert.Equal(["dataAreaId", "FiscalEstablishmentId", "CNPJ", "Name"], q["$select"]!.Split(','));
        Assert.Null(q["$filter"]);   // sem companies, todos os que a credencial enxerga
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("tenant-a", h.Tokens.Connections.Single().TenantId);   // o provedor do coletor, com a conexão do tenant
    }

    [Fact]
    public async Task Companies_of_the_profile_filter_the_register()
    {
        var h = new Harness($$"""{"url":"{{Env}}","companies":["brmf","br'sp"]}""");
        h.Http.Respond(D365Fixtures.Text(Brmf));

        await h.Directory.ListCompaniesAsync("tenant-a");

        Assert.Equal("dataAreaId eq 'brmf' or dataAreaId eq 'br''sp'", Query(h.Http.Requests.Single())["$filter"]);
    }

    // ---------- o mapeamento (gravado) ----------

    // A empresa é identificada pela raiz do CNPJ, e mostrada pelo CNPJ completo da matriz (change company-root-in-directory,
    // revisão de 2026-10-05). Antes da change, eram quatro empresas, uma por estabelecimento.
    [Fact]
    public async Task Four_recorded_establishments_are_one_company_coded_by_the_matriz()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Brmf));

        IReadOnlyList<Company> companies = await h.Directory.ListCompaniesAsync("tenant-a");

        // A Matriz é a de ordem 0001: o código é o CNPJ completo dela, e o nome é o dela.
        Assert.Equal([new Company { Code = "44278225000180", Name = "Contoso Entertainment System Brazil" }], companies);
    }

    [Fact]
    public async Task The_company_lists_the_four_branches_with_their_cnpj_in_code_order()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Brmf));

        Assert.Equal(
            [
                new Branch { Code = "Matriz", Name = "Contoso Entertainment System Brazil", TaxId = "44278225000180" },
                new Branch { Code = "RJ-01", Name = "Filial Rio de Janeiro", TaxId = "44278225003448" },   // o RJ-01, que não tem nota
                new Branch { Code = "SAL-01", Name = "Filial Salvador", TaxId = "44278225000341" },
                new Branch { Code = "SP-01", Name = "Filial de serviços", TaxId = "44278225000260" },
            ],
            await h.Directory.ListBranchesAsync("tenant-a", "44278225000180"));
    }

    // A comparação é pela raiz: qualquer CNPJ da empresa traz as filiais dela, inclusive o de uma filial, gravado num
    // agendamento da change anterior.
    [Theory]
    [InlineData("44278225000180")]
    [InlineData("44278225000260")]
    [InlineData("44278225000341")]
    [InlineData("44278225003448")]
    public async Task Any_cnpj_of_the_company_lists_the_four_branches(string company)
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Brmf));

        Assert.Equal(["Matriz", "RJ-01", "SAL-01", "SP-01"], (await h.Directory.ListBranchesAsync("tenant-a", company)).Select(b => b.Code));
    }

    [Fact]
    public async Task Without_order_0001_the_company_is_coded_by_the_lowest_order_present()
    {
        // Derivada: um cliente que só tem filiais no D365, sem a matriz. A empresa não pode quebrar nem aparecer em branco.
        var h = new Harness();
        h.Http.Respond(Register(
            Establishment("brmf", "SAL-01", "442782250003-41", "Filial Salvador"),
            Establishment("brmf", "SP-01", "442782250002-60", "Filial de serviços")));

        Assert.Equal([new Company { Code = "44278225000260", Name = "Filial de serviços" }], await h.Directory.ListCompaniesAsync("tenant-a"));
    }

    [Fact]
    public async Task Two_roots_in_the_register_are_two_companies_in_root_order()
    {
        var h = new Harness();
        JsonObject register = JsonNode.Parse(D365Fixtures.Text(Brmf))!.AsObject();
        register["value"]!.AsArray().Add(Establishment("brmf", "EMI-01", "12.345.678/0001-90", "Emitente LTDA"));
        h.Http.Respond(register.ToJsonString());
        h.Http.Respond(register.ToJsonString());

        Assert.Equal(["12345678000190", "44278225000180"], (await h.Directory.ListCompaniesAsync("tenant-a")).Select(c => c.Code));
        Assert.Equal(
            [new Branch { Code = "EMI-01", Name = "Emitente LTDA", TaxId = "12345678000190" }],
            await h.Directory.ListBranchesAsync("tenant-a", "12345678000190"));
    }

    [Fact]
    public async Task Company_not_in_the_register_has_no_branch()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Brmf));

        Assert.Empty(await h.Directory.ListBranchesAsync("tenant-a", "98765432000188"));
    }

    [Fact]
    public async Task An_empty_company_has_no_branch()
    {
        var h = new Harness();
        h.Http.Respond(D365Fixtures.Text(Brmf));

        Assert.Empty(await h.Directory.ListBranchesAsync("tenant-a", string.Empty));   // e não "todas"
    }

    [Fact]
    public async Task Same_cnpj_in_two_establishments_is_one_company_with_two_branches_named_by_the_lowest_code()
    {
        var h = new Harness();
        h.Http.Respond(Register(
            Establishment("brmf", "SP-02", "442782250002-60", "Filial de serviços 2"),
            Establishment("brmf", "SP-01", "442782250002-60", "Filial de serviços")));
        h.Http.Respond(Register(
            Establishment("brmf", "SP-02", "442782250002-60", "Filial de serviços 2"),
            Establishment("brmf", "SP-01", "442782250002-60", "Filial de serviços")));

        // Sem a 0001, a menor ordem é a 0002, dos dois; o desempate é o menor código.
        Assert.Equal([new Company { Code = "44278225000260", Name = "Filial de serviços" }], await h.Directory.ListCompaniesAsync("tenant-a"));
        Assert.Equal(["SP-01", "SP-02"], (await h.Directory.ListBranchesAsync("tenant-a", "44278225000260")).Select(b => b.Code));
    }

    [Fact]
    public async Task Alphanumeric_cnpj_keeps_its_letters()
    {
        var h = new Harness();
        h.Http.Respond(Register(Establishment("brmf", "SP-02", "12.ABC.345/01DE-35", "Filial nova")));

        Assert.Equal([new Company { Code = "12ABC34501DE35", Name = "Filial nova" }], await h.Directory.ListCompaniesAsync("tenant-a"));
    }

    [Fact]
    public async Task Establishment_without_cnpj_or_code_stays_out_with_a_warning()
    {
        var h = new Harness();
        h.Http.Respond(Register(
            Establishment("brmf", "Matriz", "442782250001-80", "Contoso"),
            Establishment("brmf", "SEM-CNPJ", "", "Sem CNPJ"),
            Establishment("brmf", "", "442782250009-99", "Sem código")));

        IReadOnlyList<Company> companies = await h.Directory.ListCompaniesAsync("tenant-a");

        Assert.Equal(["44278225000180"], companies.Select(c => c.Code));
        Assert.Equal(2, h.Logger.Warnings.Count);
        Assert.Contains(h.Logger.Warnings, w => w.Contains("SEM-CNPJ") && w.Contains("brmf"));
    }

    [Fact]
    public async Task Next_link_is_followed_to_the_end_of_the_register()
    {
        var h = new Harness();
        h.Http.Respond(Register($"{Env}/data/FiscalEstablishments?$skip=1", Establishment("brmf", "Matriz", "442782250001-80", "Contoso")));
        h.Http.Respond(Register(Establishment("brmf", "RJ-01", "442782250034-48", "Filial Rio de Janeiro")));

        IReadOnlyList<Branch> branches = await h.Directory.ListBranchesAsync("tenant-a", "44278225000180");

        Assert.Equal(["Matriz", "RJ-01"], branches.Select(b => b.Code));   // uma filial de cada página
        Assert.Equal(2, h.Http.Requests.Count);
    }

    // ---------- as falhas ----------

    [Fact]
    public async Task Forbidden_names_the_role_and_the_standard_privilege()
    {
        var h = new Harness();
        h.Http.Respond("""{"error":{"message":"denied"}}""", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Directory.ListCompaniesAsync("tenant-a"));

        Assert.Contains("HTTP 403", ex.Message);
        Assert.Contains("FSFiscalHubIntegration", ex.Message);
        Assert.Contains("FiscalEstablishmentEntityView", ex.Message);
    }

    [Fact]
    public async Task Server_error_names_the_status_without_the_token_nor_the_header()
    {
        var h = new Harness();
        h.Http.Respond("""{"error":{"message":"boom"}}""", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Directory.ListBranchesAsync("tenant-a", "44278225000180"));

        Assert.Contains("HTTP 500", ex.Message);
        Assert.DoesNotContain("tok-123", ex.Message);
        Assert.DoesNotContain("Bearer", ex.Message);
        Assert.DoesNotContain("Authorization", ex.Message);
    }

    [Fact]
    public async Task Throttling_above_the_cap_says_the_erp_asked_to_wait()
    {
        var h = new Harness();
        h.Http.Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(600)));

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Directory.ListCompaniesAsync("tenant-a"));

        Assert.Contains("pediu espera", ex.Message);
    }

    [Fact]
    public async Task Credential_refused_by_the_entra_id_is_said_without_the_detail()
    {
        var h = new Harness();
        h.Tokens.Failure = new AuthenticationFailedException("AADSTS7000215: Invalid client secret provided.");

        var ex = await Assert.ThrowsAsync<OriginUnavailableException>(() => h.Directory.ListCompaniesAsync("tenant-a"));

        Assert.Contains("credencial do ERP foi recusada", ex.Message);
        Assert.DoesNotContain("AADSTS", ex.Message);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Settings_without_url_fail_without_any_request()
    {
        var h = new Harness("""{"url":""}""");

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => h.Directory.ListCompaniesAsync("tenant-a"));

        Assert.Empty(h.Http.Requests);
        Assert.Empty(h.Tokens.Connections);
    }

    [Fact]
    public async Task Profile_without_auth_fails_with_the_reason_and_without_any_request()
    {
        // O provider de produção, e não o falso: o dropdown mostra este motivo no lugar da lista (change
        // explicit-credential-and-execution-cnpj, D2).
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
        var directory = new D365CompanyDirectory(
            new HttpClient(http), profiles,
            new ClientCredentialsD365TokenProvider(new EmptyVault(), new ListLogger<ClientCredentialsD365TokenProvider>()),
            new D365ChangeFeedOptions(), new FakeTime(DateTimeOffset.UnixEpoch), new ListLogger<D365CompanyDirectory>());

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(() => directory.ListCompaniesAsync("tenant-a"));

        Assert.StartsWith("A credencial do ERP não está configurada", ex.Message);
        Assert.Empty(http.Requests);
    }

    // ---------- apoio ----------

    private static NameValueCollection Query(HttpRequestMessage request) => HttpUtility.ParseQueryString(request.RequestUri!.Query);

    private static JsonObject Establishment(string company, string code, string cnpj, string name) => new()
    {
        ["dataAreaId"] = company,
        ["FiscalEstablishmentId"] = code,
        ["CNPJ"] = cnpj,
        ["Name"] = name,
    };

    private static string Register(params JsonObject[] rows) => Register(nextLink: null, rows);

    private static string Register(string? nextLink, params JsonObject[] rows)
    {
        var body = new JsonObject { ["value"] = new JsonArray([.. rows]) };
        if (nextLink is not null)
        {
            body["@odata.nextLink"] = nextLink;
        }

        return body.ToJsonString();
    }

    private sealed class Harness
    {
        public Harness(string? settings = null)
        {
            var profiles = new FakeProfiles
            {
                Profile = new TenantConnectorProfile
                {
                    TenantId = "tenant-a",
                    Environment = "Sandbox",
                    InboundAdapter = "Dynamics365",
                    InboundSettings = settings ?? $$"""{"url":"{{Env}}","companies":["brmf"]}""",
                    OutboundAdapter = "Avalara",
                },
            };
            Directory = new D365CompanyDirectory(new HttpClient(Http), profiles, Tokens, new D365ChangeFeedOptions(), new FakeTime(DateTimeOffset.UnixEpoch), Logger);
        }

        public SequencedHttpMessageHandler Http { get; } = new();
        public FailingTokens Tokens { get; } = new();
        public ListLogger<D365CompanyDirectory> Logger { get; } = new();
        public D365CompanyDirectory Directory { get; }
    }

    /// <summary>O token do coletor, que pode falhar como o Entra ID falha.</summary>
    internal sealed class FailingTokens : ID365TokenProvider
    {
        public List<D365Connection> Connections { get; } = [];

        public Exception? Failure { get; set; }

        public Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
        {
            if (Failure is not null)
            {
                return Task.FromException<string>(Failure);
            }

            Connections.Add(connection);
            return Task.FromResult("tok-123");
        }
    }
}
