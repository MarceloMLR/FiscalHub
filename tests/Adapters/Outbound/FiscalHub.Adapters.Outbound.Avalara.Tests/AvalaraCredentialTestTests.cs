using System.Net;
using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Options;
using static FiscalHub.Adapters.Outbound.Avalara.Tests.AvalaraTokenProviderTests;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o teste de credencial da Avalara (spec connector-credential-test, design D10): a credencial gravada na conta
/// de plataforma, um token novo no endpoint de token da conta, e um motivo que nunca leva o token, o segredo, nem um
/// cabeçalho. É o teste da conta, e não de um conector: a URL base de nenhum conector entra. O ambiente é o da instância,
/// e não escolha do teste (change platform-account-and-connectors, D6 e D11).
/// </summary>
public class AvalaraCredentialTestTests
{
    private const string AccountSecret = "fh-tenant-a--platform--avalara--clientsecret";
    private const string TokenUrl = "https://login.avalara/oauth/token";

    [Fact]
    public async Task Tests_the_stored_credential_at_the_token_endpoint_of_the_account_in_the_environment_of_the_instance()
    {
        var h = new Harness(environment: "Production");

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Worked, outcome.Verdict);
        (string url, string? clientId, string? secret) = Assert.Single(h.Endpoint.Requests).Summary;
        Assert.Equal(TokenUrl, url);   // o endpoint da conta, e não a URL base do conector
        Assert.Equal("id-a", clientId);
        Assert.Equal("segredo-a", secret);
        Assert.Contains("Produção", outcome.Reason);   // o ambiente da instância, como contexto
    }

    [Fact]
    public async Task Worked_reason_has_no_token_no_secret_and_no_header()
    {
        var h = new Harness();

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Worked, outcome.Verdict);
        AssertClean(outcome.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Refused_credential_asks_to_check_both_fields_without_the_secret(HttpStatusCode status)
    {
        var h = new Harness { Endpoint = { Status = status, Body = """{"error":"client_id invalid segredo-a"}""" } };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("Confira o Client ID e o Client Secret", outcome.Reason);
        AssertClean(outcome.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Unavailable_platform_is_unavailable(HttpStatusCode status)
    {
        var h = new Harness { Endpoint = { Status = status, Body = "fora do ar" } };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
    }

    [Fact]
    public async Task Timeout_is_unavailable()
    {
        var h = new Harness { Endpoint = { Delay = TimeSpan.FromSeconds(5) }, Timeout = TimeSpan.FromMilliseconds(50) };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
        Assert.Contains("a tempo", outcome.Reason);
    }

    [Fact]
    public async Task Response_without_a_token_does_not_work()
    {
        var h = new Harness { Endpoint = { Body = """{"token_type":"Bearer","expires_in":3600}""" } };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("sem token", outcome.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task Wrong_token_endpoint_asks_to_check_the_token_endpoint_of_the_account(HttpStatusCode status)
    {
        var h = new Harness { Endpoint = { Status = status, Body = "nao encontrado" } };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("Confira o endpoint de token da conta", outcome.Reason);
        AssertClean(outcome.Reason);
    }

    [Theory]
    [InlineData("tokenUrl")]
    [InlineData("clientId")]
    public async Task Account_missing_a_field_of_the_credential_is_incomplete_with_no_request(string missing)
    {
        var h = new Harness
        {
            Profile = AvalaraProfiles.Profile(
                AvalaraProfiles.Connector("https://avalara/"),
                clientId: missing == "clientId" ? null : "id-a",
                tokenUrl: missing == "tokenUrl" ? null : TokenUrl),
        };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Incomplete, outcome.Verdict);
        Assert.Contains(missing, outcome.Reason);
        Assert.Equal(0, h.Endpoint.Calls);
    }

    [Fact]
    public async Task Missing_secret_is_incomplete_with_no_request()
    {
        var h = new Harness();
        h.Secrets.Values.Remove(AccountSecret);

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile);

        Assert.Equal(CredentialTestVerdict.Incomplete, outcome.Verdict);
        Assert.Equal(0, h.Endpoint.Calls);
    }

    [Fact]
    public void Is_the_platform_account_test_of_the_avalara_adapter()
    {
        var h = new Harness();

        Assert.Equal("Avalara", h.Test.Adapter);
        Assert.Equal(ConnectorSettingsKind.Platform, h.Test.Side);
    }

    // O motivo nunca leva o token emitido (tok-N), o segredo, nem um cabeçalho de autorização.
    private static void AssertClean(string reason)
    {
        Assert.DoesNotContain("tok-", reason);
        Assert.DoesNotContain("segredo", reason);
        Assert.DoesNotContain("Bearer", reason);
        Assert.DoesNotContain("Authorization", reason);
    }

    private sealed class Harness(string environment = "Sandbox")
    {
        private AvalaraCredentialTest? _test;

        public TokenEndpointStub Endpoint { get; } = new();

        public FakeSecrets Secrets { get; } = new() { Values = { [AccountSecret] = "segredo-a" } };

        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

        public TenantConnectorProfile Profile { get; init; } = AvalaraProfiles.Profile(AvalaraProfiles.Connector("https://avalara/"), tokenUrl: TokenUrl);

        public AvalaraCredentialTest Test => _test ??= new AvalaraCredentialTest(
            new AvalaraTokenProvider(
                new HttpClient(Endpoint) { Timeout = Timeout }, Secrets, Options.Create(new AvalaraOptions()), new FakeClock(),
                new CapturingLogger<AvalaraTokenProvider>()),
            InstanceEnvironment.From(environment));
    }
}
