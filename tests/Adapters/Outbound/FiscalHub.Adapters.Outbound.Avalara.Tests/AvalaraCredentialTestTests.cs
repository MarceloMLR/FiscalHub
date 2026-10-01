using System.Net;
using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Options;
using static FiscalHub.Adapters.Outbound.Avalara.Tests.AvalaraTokenProviderTests;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o teste de credencial da Avalara (spec connector-credential-test, design D10): a seção do ambiente escolhido,
/// um token novo, e um motivo que nunca leva o token, o segredo, nem um cabeçalho.
/// </summary>
public class AvalaraCredentialTestTests
{
    private const string SandboxSecret = "fh-tenant-a--outbound--sandbox--clientsecret";
    private const string ProductionSecret = "fh-tenant-a--outbound--production--clientsecret";

    [Fact]
    public async Task Tests_the_section_of_the_chosen_environment_not_the_active_one()
    {
        var h = new Harness();

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Production");

        Assert.Equal(CredentialTestVerdict.Worked, outcome.Verdict);
        (string url, string? clientId, string? secret) = Assert.Single(h.Endpoint.Requests).Summary;
        Assert.StartsWith("https://avalara-prod/", url);
        Assert.Equal("id-prod", clientId);
        Assert.Equal("segredo-prod", secret);
        Assert.Contains("Produção", outcome.Reason);
    }

    [Fact]
    public async Task Worked_reason_has_no_token_no_secret_and_no_header()
    {
        var h = new Harness();

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Sandbox");

        Assert.Equal(CredentialTestVerdict.Worked, outcome.Verdict);
        AssertClean(outcome.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Refused_credential_asks_to_check_both_fields_without_the_secret(HttpStatusCode status)
    {
        var h = new Harness { Endpoint = { Status = status, Body = """{"error":"client_id invalid segredo-sandbox"}""" } };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Sandbox");

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

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Sandbox");

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
    }

    [Fact]
    public async Task Timeout_is_unavailable()
    {
        var h = new Harness { Endpoint = { Delay = TimeSpan.FromSeconds(5) }, Timeout = TimeSpan.FromMilliseconds(50) };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Sandbox");

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
        Assert.Contains("a tempo", outcome.Reason);
    }

    [Fact]
    public async Task Response_without_a_token_does_not_work()
    {
        var h = new Harness { Endpoint = { Body = """{"token_type":"Bearer","expires_in":3600}""" } };

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Sandbox");

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("sem token", outcome.Reason);
    }

    [Fact]
    public async Task Missing_secret_is_incomplete_with_no_request()
    {
        var h = new Harness();
        h.Secrets.Values.Remove(ProductionSecret);

        CredentialTestOutcome outcome = await h.Test.TestAsync(h.Profile, "Production");

        Assert.Equal(CredentialTestVerdict.Incomplete, outcome.Verdict);
        Assert.Equal(0, h.Endpoint.Calls);
    }

    [Fact]
    public void Is_the_outbound_test_of_the_avalara_adapter()
    {
        var h = new Harness();

        Assert.Equal("Avalara", h.Test.Adapter);
        Assert.Equal(ConnectorSettingsKind.Outbound, h.Test.Side);
    }

    // O motivo nunca leva o token emitido (tok-N), o segredo, nem um cabeçalho de autorização.
    private static void AssertClean(string reason)
    {
        Assert.DoesNotContain("tok-", reason);
        Assert.DoesNotContain("segredo", reason);
        Assert.DoesNotContain("Bearer", reason);
        Assert.DoesNotContain("Authorization", reason);
    }

    private sealed class Harness
    {
        private AvalaraCredentialTest? _test;

        public Harness()
        {
            Secrets.Values[SandboxSecret] = "segredo-sandbox";
            Secrets.Values[ProductionSecret] = "segredo-prod";
        }

        public TokenEndpointStub Endpoint { get; } = new();

        public FakeSecrets Secrets { get; } = new();

        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

        // O ambiente ativo é o Sandbox: o teste de Produção não depende de trocá-lo.
        public TenantConnectorProfile Profile { get; } = new()
        {
            TenantId = "tenant-a",
            Environment = "Sandbox",
            InboundAdapter = "Dynamics365",
            OutboundAdapter = "Avalara",
            OutboundSettings = $$$"""
                {"sandbox":{"baseUrl":"https://avalara-sandbox/","clientId":"id-sandbox","clientSecretRef":"kv:{{{SandboxSecret}}}"},
                 "production":{"baseUrl":"https://avalara-prod/","clientId":"id-prod","clientSecretRef":"kv:{{{ProductionSecret}}}"}}
                """,
        };

        public AvalaraCredentialTest Test => _test ??= new AvalaraCredentialTest(new AvalaraTokenProvider(
            new HttpClient(Endpoint) { Timeout = Timeout }, Secrets, Options.Create(new AvalaraOptions()), new FakeClock(),
            new CapturingLogger<AvalaraTokenProvider>()));
    }
}
