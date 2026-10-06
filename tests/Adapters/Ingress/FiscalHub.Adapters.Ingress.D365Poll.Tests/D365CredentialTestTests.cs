using System.Net;
using Azure.Core;
using Azure.Identity;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica o teste de credencial do D365 (spec connector-credential-test, design D11): um token NOVO do Entra ID a cada
/// teste, pela credencial gravada do tenant, e uma leitura mínima da FSFiscalDocumentBRs, porque o token
/// prova a credencial, e não a permissão. O cache de token do coletor não é lido nem trocado. Cofre, credencial e F&amp;O
/// falsos, sem rede.
/// </summary>
public class D365CredentialTestTests
{
    private const string SecretName = "fh-tenant-a--inbound--auth--clientsecret";
    private const string Secret = "s3cr3t-valor";
    private const string ReadUrl = "https://fiscosysdev.operations.dynamics.com/data/FSFiscalDocumentBRs?$top=1&$select=FiscalDocumentRecId&cross-company=true";

    private const string Settings = """
        {"url":"https://fiscosysdev.operations.dynamics.com","auth":{"tenantId":"entra-a","clientId":"app-a","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"},
         "poll":{"enabled":true}}
        """;

    // ---------- o token novo ----------

    [Fact]
    public async Task Each_test_creates_its_own_credential_and_asks_the_entra_id()
    {
        var h = new Harness();
        h.Erp.Respond("""{"value":[{"FiscalDocumentRecId":1}]}""").Respond("""{"value":[{"FiscalDocumentRecId":1}]}""");

        await h.Test.TestAsync(Profile(), null);
        await h.Test.TestAsync(Profile(), null);

        Assert.Equal(2, h.Created.Count);                       // uma instância por teste
        Assert.All(h.Created, c => Assert.Equal(1, c.Requests));   // e cada uma foi ao Entra ID
    }

    [Fact]
    public async Task Collector_keeps_reusing_its_credential_with_a_test_in_between()
    {
        var h = new Harness();
        h.Erp.Respond("""{"value":[]}""");
        var connection = new D365Connection("tenant-a", new Uri("https://fiscosysdev.operations.dynamics.com"),
            new D365AuthSettings("entra-a", "app-a", $"kv:{SecretName}"));

        await h.Tokens.GetTokenAsync(connection);   // o coletor
        await h.Test.TestAsync(Profile(), null);     // o teste, no meio
        await h.Tokens.GetTokenAsync(connection);   // o coletor de novo

        Assert.Equal(2, h.Created.Count);               // a do coletor e a do teste; o coletor não ganhou outra
        Assert.Equal(2, h.Created[0].Requests);          // a instância do coletor serviu as duas chamadas dele
        Assert.Equal(1, h.Created[1].Requests);
    }

    // ---------- a leitura ----------

    [Fact]
    public async Task Reads_one_record_of_the_entity_across_companies_with_a_single_get()
    {
        var h = new Harness();
        h.Erp.Respond("""{"value":[{"FiscalDocumentRecId":1}],"@odata.nextLink":"https://fiscosysdev.operations.dynamics.com/data/FSFiscalDocumentBRs?$skip=1"}""");

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Worked, outcome.Verdict);
        HttpRequestMessage request = Assert.Single(h.Erp.Requests);   // o nextLink não é seguido
        Assert.Equal(ReadUrl, request.RequestUri!.OriginalString);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        AssertClean(outcome.Reason, request.Headers.Authorization.Parameter!);
    }

    [Fact]
    public async Task Empty_read_works_and_warns_that_it_does_not_prove_access_to_the_companies()
    {
        var h = new Harness();
        h.Erp.Respond("""{"value":[]}""");

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Worked, outcome.Verdict);
        Assert.Contains("não prova o acesso às empresas", outcome.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "cadastrado")]
    [InlineData(HttpStatusCode.Forbidden, "privilégio")]
    [InlineData(HttpStatusCode.NotFound, "pacote FS")]
    public async Task Refusals_of_the_read_say_what_to_check(HttpStatusCode status, string expected)
    {
        var h = new Harness();
        h.Erp.Respond("""{"error":{"message":"negado"}}""", status);

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains(expected, outcome.Reason);
        Assert.Contains($"HTTP {(int)status}", outcome.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Unavailable_erp_is_unavailable(HttpStatusCode status)
    {
        var h = new Harness();
        h.Erp.Respond("{}", status);

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
    }

    [Fact]
    public async Task Read_timeout_is_unavailable()
    {
        var h = new Harness(timeout: TimeSpan.FromMilliseconds(50));
        h.Erp.Delay = TimeSpan.FromSeconds(5);
        h.Erp.Respond("""{"value":[]}""");

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
    }

    // ---------- o token ----------

    [Fact]
    public async Task Entra_refusal_is_refused_with_only_the_code_and_no_read()
    {
        var h = new Harness
        {
            Failure = new AuthenticationFailedException(
                $"ClientSecretCredential authentication failed: AADSTS7000215: Invalid client secret provided. Ensure the secret being sent in the request is the client secret value. Trace ID: 1f2e"),
        };

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("(AADSTS7000215)", outcome.Reason);
        Assert.DoesNotContain("Trace ID", outcome.Reason);   // só o código, e não a mensagem inteira do Entra ID
        Assert.Empty(h.Erp.Requests);
    }

    // O caso da prova manual (2026-10-01): a credencial errada voltava como "o Entra ID não respondeu", porque o código
    // AADSTS não estava na mensagem de fora. Recusa é recusa; só uma causa de rede é indisponibilidade.
    [Fact]
    public async Task Entra_refusal_with_the_code_only_in_the_inner_exception_is_refused()
    {
        var h = new Harness
        {
            Failure = new AuthenticationFailedException(
                "ClientSecretCredential authentication failed: A configuration issue is preventing authentication.",
                new InvalidOperationException("AADSTS7000215: Invalid client secret provided.")),
        };

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("(AADSTS7000215)", outcome.Reason);
        Assert.Empty(h.Erp.Requests);
    }

    [Fact]
    public async Task Entra_failure_without_a_code_and_without_a_network_cause_is_refused()
    {
        var h = new Harness { Failure = new AuthenticationFailedException("ClientSecretCredential authentication failed: invalid_client") };

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Empty(h.Erp.Requests);
    }

    [Fact]
    public async Task Entra_failure_with_a_network_cause_is_unavailable()
    {
        var h = new Harness
        {
            Failure = new AuthenticationFailedException("ClientSecretCredential authentication failed: connection reset",
                new HttpRequestException("An error occurred while sending the request.", new System.Net.Sockets.SocketException(10054))),
        };

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Unavailable, outcome.Verdict);
        Assert.Empty(h.Erp.Requests);
    }

    [Fact]
    public async Task Malformed_tenant_or_client_id_is_refused()
    {
        var h = new Harness { Failure = new ArgumentException("Invalid tenant id provided.", "tenantId") };

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
    }

    [Fact]
    public async Task Erp_host_that_does_not_exist_is_refused_and_not_unavailable()
    {
        var h = new Harness();
        h.Erp.Failure = new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Refused, outcome.Verdict);
        Assert.Contains("URL", outcome.Reason);
    }

    // ---------- a credencial incompleta ----------

    [Theory]
    [InlineData("""{"url":"https://fiscosysdev.operations.dynamics.com"}""", "A credencial do ERP não está configurada")]          // sem auth
    [InlineData("""{"url":"https://fiscosysdev.operations.dynamics.com","auth":{"tenantId":"entra-a","clientId":"app-a"}}""",
        "A credencial do ERP está incompleta: falta Client Secret.")]                                                                 // sem o segredo
    [InlineData("""{"auth":{"tenantId":"entra-a"}}""", "url do ambiente F&O ausente")]                                              // sem URL
    public async Task Incomplete_credential_makes_no_request(string settings, string reason)
    {
        var h = new Harness();

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(settings), null);

        Assert.Equal(CredentialTestVerdict.Incomplete, outcome.Verdict);
        Assert.Contains(reason, outcome.Reason);
        Assert.Empty(h.Created);
        Assert.Empty(h.Erp.Requests);
    }

    [Fact]
    public async Task Secret_missing_in_the_vault_is_incomplete_and_never_falls_back_to_another_identity()
    {
        var h = new Harness(secretInVault: false);

        CredentialTestOutcome outcome = await h.Test.TestAsync(Profile(), null);

        Assert.Equal(CredentialTestVerdict.Incomplete, outcome.Verdict);
        Assert.Contains("O Client Secret do ERP do tenant 'tenant-a' não está no cofre", outcome.Reason);
        Assert.Empty(h.Created);        // nenhuma credencial criada: nem a do tenant, nem outra
        Assert.Empty(h.Erp.Requests);
    }

    [Fact]
    public void Is_the_inbound_test_of_the_dynamics365_adapter()
    {
        var h = new Harness();

        Assert.Equal("Dynamics365", h.Test.Adapter);
        Assert.Equal(ConnectorSettingsKind.Inbound, h.Test.Side);
    }

    private static TenantConnectorProfile Profile(string settings = Settings) => new()
    {
        TenantId = "tenant-a",
        Environment = "Sandbox",
        InboundAdapter = "Dynamics365",
        InboundSettings = settings,
        OutboundAdapter = "Avalara",
    };

    private static void AssertClean(string reason, string token)
    {
        Assert.DoesNotContain(token, reason);
        Assert.DoesNotContain(Secret, reason);
        Assert.DoesNotContain("Bearer", reason);
        Assert.DoesNotContain("Authorization", reason);
    }

    private sealed class Harness
    {
        private D365CredentialTest? _test;

        public Harness(bool secretInVault = true, TimeSpan? timeout = null)
        {
            Secrets = new Vault(secretInVault ? [(SecretName, Secret)] : []);
            Tokens = new ClientCredentialsD365TokenProvider(Secrets, Logger, (tenant, client, secret) =>
            {
                var credential = new CountingCredential(Failure, Created.Count + 1);
                Created.Add(credential);
                return credential;
            });
            Timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public Exception? Failure { get; init; }

        public List<CountingCredential> Created { get; } = [];

        public D365ChangeFeedTests.ListLogger<ClientCredentialsD365TokenProvider> Logger { get; } = new();

        public Vault Secrets { get; }

        public ClientCredentialsD365TokenProvider Tokens { get; }

        public DelayedHandler Erp { get; } = new();

        public TimeSpan Timeout { get; }

        public D365CredentialTest Test => _test ??= new D365CredentialTest(Tokens, new HttpClient(Erp) { Timeout = Timeout });
    }

    /// <summary>Credencial falsa: conta os pedidos de token, e lança a falha configurada.</summary>
    internal sealed class CountingCredential(Exception? failure, int n) : TokenCredential
    {
        public int Requests { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Requests++;
            if (failure is not null)
            {
                throw failure;
            }

            return new AccessToken($"eyJ-token-{n}-{Requests}", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    internal sealed class Vault((string Name, string Value)[] values) : ISecretStore
    {
        private readonly Dictionary<string, string> _values = values.ToDictionary(v => v.Name, v => v.Value);

        public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult(_values.GetValueOrDefault(name));

        public Task SetAsync(string name, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>O <see cref="SequencedHttpMessageHandler"/> com um atraso opcional, para o tempo esgotado.</summary>
    internal sealed class DelayedHandler : DelegatingHandler
    {
        private readonly SequencedHttpMessageHandler _inner = new();

        public DelayedHandler() => InnerHandler = _inner;

        public TimeSpan Delay { get; set; }

        public Exception? Failure { get; set; }

        public List<HttpRequestMessage> Requests => _inner.Requests;

        public DelayedHandler Respond(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _inner.Respond(json, status);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Failure is not null)
            {
                Requests.Add(request);
                throw Failure;
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
