using System.Net;
using System.Text;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o token por credencial (ADR-0027): a credencial vem da seção do ambiente ativo e o segredo, do cofre; o
/// cache distingue tenant, ambiente, endpoint, cliente e a versão do segredo; uma única busca por credencial sob
/// concorrência, reuso enquanto válido e renovação na margem. A recusa da credencial é lembrada por um intervalo, e
/// salvar o perfil a esquece na hora (<c>Forget</c>).
/// </summary>
public class AvalaraTokenProviderTests
{
    private const string SecretA = "fh-tenant-a--outbound--sandbox--clientsecret";
    private const string SecretB = "fh-tenant-b--outbound--sandbox--clientsecret";

    // ---------- cache ----------

    [Fact]
    public async Task Valid_cached_token_is_reused_and_is_fresh_only_when_fetched()
    {
        var h = new Harness();

        AvalaraAccessToken first = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        AvalaraAccessToken second = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        Assert.Equal(1, h.Endpoint.Calls);
        Assert.Equal(first.Value, second.Value);
        Assert.True(first.IsFresh);
        Assert.False(second.IsFresh);
    }

    [Fact]
    public async Task Token_is_renewed_only_inside_the_safety_margin()
    {
        var h = new Harness { Endpoint = { ExpiresIn = 3600 } };   // 1h; margem padrão de 5 min

        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        h.Clock.Advance(TimeSpan.FromMinutes(50));   // faltam 10 min: fora da margem, ainda vale
        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        Assert.Equal(1, h.Endpoint.Calls);

        h.Clock.Advance(TimeSpan.FromMinutes(8));    // faltam 2 min: dentro da margem, renova
        AvalaraAccessToken renewed = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        Assert.Equal(2, h.Endpoint.Calls);
        Assert.True(renewed.IsFresh);
    }

    [Fact]
    public async Task Concurrent_calls_fetch_the_token_only_once()
    {
        // 20 esteiras pedindo token ao mesmo tempo, cache vazio. O atraso garante que as chamadas se sobreponham.
        var h = new Harness { Endpoint = { Delay = TimeSpan.FromMilliseconds(80) } };

        AvalaraAccessToken[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => h.Provider.GetTokenAsync(h.Settings("tenant-a"))));

        Assert.Equal(1, h.Endpoint.Calls);
        Assert.All(tokens, t => Assert.Equal(tokens[0].Value, t.Value));
    }

    [Fact]
    public async Task Tenants_never_share_a_token_even_with_the_same_credential()
    {
        var h = new Harness();
        h.Secrets.Values[SecretB] = "segredo-a";   // o mesmo valor do tenant-a, de propósito

        AvalaraAccessToken a = await h.Provider.GetTokenAsync(h.Settings("tenant-a", clientId: "mesmo-id"));
        AvalaraAccessToken b = await h.Provider.GetTokenAsync(h.Settings("tenant-b", clientId: "mesmo-id"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public async Task Each_tenant_asks_with_its_own_credential_at_its_own_endpoint()
    {
        var h = new Harness();
        h.Secrets.Values[SecretB] = "segredo-b";

        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        await h.Provider.GetTokenAsync(h.Settings("tenant-b", clientId: "id-b", baseUrl: "https://avalara-b/"));

        Assert.Equal(("https://avalara-a/oauth/token", "id-a", "segredo-a"), h.Endpoint.Requests[0].Summary);
        Assert.Equal(("https://avalara-b/oauth/token", "id-b", "segredo-b"), h.Endpoint.Requests[1].Summary);
    }

    [Fact]
    public async Task Environment_change_fetches_a_new_token_from_the_new_section()
    {
        var h = new Harness();
        h.Secrets.Values["fh-tenant-a--outbound--production--clientsecret"] = "segredo-prod";

        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        await h.Provider.GetTokenAsync(h.Settings("tenant-a", environment: "Production", clientId: "id-prod", baseUrl: "https://avalara-prod/"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.Equal(("https://avalara-prod/oauth/token", "id-prod", "segredo-prod"), h.Endpoint.Requests[1].Summary);
    }

    [Fact]
    public async Task Secret_rotation_fetches_a_new_token_with_the_new_secret()
    {
        var h = new Harness();

        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        h.Secrets.Values[SecretA] = "segredo-novo";
        AvalaraAccessToken rotated = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.Equal("segredo-novo", h.Endpoint.Requests[1].Form["client_secret"]);
        Assert.True(rotated.IsFresh);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(300)]   // igual à margem: venceria antes de ser reusado
    public async Task Token_without_a_usable_validity_is_used_and_not_cached_warning_once(int? expiresIn)
    {
        var h = new Harness { Endpoint = { ExpiresIn = expiresIn } };

        AvalaraAccessToken first = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        AvalaraAccessToken second = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.True(first.IsFresh);
        Assert.True(second.IsFresh);
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain("segredo-a", h.Logger.All);
        Assert.DoesNotContain(first.Value, h.Logger.All);
    }

    [Fact]
    public async Task Invalidate_removes_the_cached_token_only_if_it_is_still_the_same()
    {
        var h = new Harness();
        AvalaraAccessToken first = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        h.Provider.Invalidate(first);
        AvalaraAccessToken second = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        h.Provider.Invalidate(first);   // velho: não derruba o novo
        AvalaraAccessToken third = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal(second.Value, third.Value);
    }

    [Fact]
    public async Task Token_request_is_client_credentials_with_the_secret_in_the_form_body()
    {
        // A premissa assumida (design D13): client_credentials com o segredo no corpo. Confirmada só no teste manual.
        var h = new Harness();

        await h.Provider.GetTokenAsync(h.Settings("tenant-a", tokenUrl: "https://login.avalara-a/connect/token"));

        TokenRequest request = Assert.Single(h.Endpoint.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://login.avalara-a/connect/token", request.Url);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal("client_credentials", request.Form["grant_type"]);
        Assert.Equal("id-a", request.Form["client_id"]);
        Assert.Equal("segredo-a", request.Form["client_secret"]);
        Assert.False(request.HadAuthorizationHeader);
    }

    // ---------- falhas do endpoint de token ----------

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Refused_credential_is_a_rejection_naming_tenant_environment_and_error(HttpStatusCode status)
    {
        var h = new Harness { Endpoint = { Status = status, Body = """{"error":"invalid_client","error_description":"Client authentication failed"}""" } };

        string reason = (await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")))).Reason;

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("'tenant-a'", reason);
        Assert.Contains("'sandbox'", reason);
        Assert.Contains($"HTTP {(int)status}", reason);
        Assert.Contains("invalid_client", reason);
        Assert.Contains("Client authentication failed", reason);
        Assert.DoesNotContain("segredo-a", reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Unavailable_endpoint_is_transient(HttpStatusCode status)
    {
        var h = new Harness { Endpoint = { Status = status, Body = "fora do ar" } };

        Exception ex = await Assert.ThrowsAsync<HttpRequestException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));

        Assert.IsNotType<DispatchRejectedException>(ex);
        await Assert.ThrowsAsync<HttpRequestException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));
        Assert.Equal(2, h.Endpoint.Calls);   // transitório não é lembrado
    }

    [Theory]
    [InlineData("""{"token_type":"Bearer","expires_in":3600}""")]
    [InlineData("""{"access_token":"","expires_in":3600}""")]
    [InlineData("não é json")]
    public async Task Success_without_a_token_is_a_rejection(string body)
    {
        var h = new Harness { Endpoint = { Body = body } };

        string reason = (await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")))).Reason;

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("respondeu sem token", reason);
    }

    [Fact]
    public async Task Refusal_is_remembered_so_five_calls_make_a_single_request()
    {
        var h = new Harness { Endpoint = { Status = HttpStatusCode.Unauthorized, Body = """{"error":"invalid_client"}""" } };

        string[] reasons = new string[5];
        for (int i = 0; i < 5; i++)
        {
            reasons[i] = (await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")))).Reason;
        }

        Assert.Equal(1, h.Endpoint.Calls);
        Assert.All(reasons, r => Assert.Equal(reasons[0], r));
    }

    [Theory]
    [InlineData("segredo")]
    [InlineData("cliente")]
    public async Task A_new_credential_does_not_inherit_the_refusal(string change)
    {
        var h = new Harness { Endpoint = { Status = HttpStatusCode.Unauthorized, Body = """{"error":"invalid_client"}""" } };
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));
        h.Endpoint.Status = HttpStatusCode.OK;
        h.Endpoint.Body = null;

        if (change == "segredo")
        {
            h.Secrets.Values[SecretA] = "segredo-corrigido";
        }

        AvalaraAccessToken token = await h.Provider.GetTokenAsync(h.Settings("tenant-a", clientId: change == "cliente" ? "id-corrigido" : "id-a"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.True(token.IsFresh);
    }

    [Fact]
    public async Task Refusal_expires_after_the_hold()
    {
        var h = new Harness { Endpoint = { Status = HttpStatusCode.Unauthorized, Body = """{"error":"invalid_client"}""" } };
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));

        h.Clock.Advance(TimeSpan.FromMinutes(4));   // dentro do intervalo padrão de 5 min
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));
        Assert.Equal(1, h.Endpoint.Calls);

        h.Clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));
        Assert.Equal(2, h.Endpoint.Calls);
    }

    [Theory]
    [InlineData("sem-referencia")]
    [InlineData("vazio")]
    [InlineData("cofre-sem-valor")]
    public async Task Missing_secret_is_a_rejection_pointing_to_the_screen_with_no_request(string situation)
    {
        var h = new Harness();
        switch (situation)
        {
            case "vazio":
                h.Secrets.Values[SecretA] = "";
                break;
            case "cofre-sem-valor":
                h.Secrets.Values.Remove(SecretA);
                break;
        }

        AvalaraOutboundSettings settings = situation == "sem-referencia"
            ? Read("tenant-a", """{"sandbox":{"baseUrl":"https://avalara-a/","clientId":"id-a"}}""")
            : h.Settings("tenant-a");

        string reason = (await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(settings))).Reason;

        Assert.Contains("OutboundSettings.sandbox.clientSecret", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → Client Secret", reason);
        Assert.Equal(0, h.Endpoint.Calls);
        if (situation != "sem-referencia")
        {
            Assert.Contains("o cofre não tem o valor", reason);
        }
    }

    [Fact]
    public async Task Foreign_reference_is_refused_without_reading_the_vault()
    {
        var h = new Harness();
        h.Secrets.Values[SecretA] = "segredo-a";

        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(
            Read("tenant-b", $$$"""{"sandbox":{"baseUrl":"https://avalara-b/","clientId":"id-b","clientSecretRef":"kv:{{{SecretA}}}"}}""")));

        Assert.Equal(0, h.Secrets.GetCount);
        Assert.Equal(0, h.Endpoint.Calls);
    }

    // ---------- Forget ----------

    [Fact]
    public async Task Forget_makes_the_next_call_ask_again_within_the_hold_with_the_same_credential()
    {
        var h = new Harness { Endpoint = { Status = HttpStatusCode.Unauthorized, Body = """{"error":"invalid_client"}""" } };
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(h.Settings("tenant-a")));
        h.Endpoint.Status = HttpStatusCode.OK;   // a plataforma liberou o cliente
        h.Endpoint.Body = null;

        h.Provider.Forget("tenant-a");
        AvalaraAccessToken token = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        Assert.Equal(2, h.Endpoint.Calls);
        Assert.True(token.IsFresh);
    }

    [Fact]
    public async Task Forget_also_drops_the_cached_tokens_of_the_tenant()
    {
        var h = new Harness();
        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        h.Provider.Forget("tenant-a");
        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        Assert.Equal(2, h.Endpoint.Calls);
    }

    [Fact]
    public async Task Forget_leaves_the_other_tenants_alone()
    {
        var h = new Harness();
        h.Endpoint.RefusedClientIds.Add("id-b-prod");
        h.Secrets.Values[SecretB] = "segredo-b";
        h.Secrets.Values["fh-tenant-b--outbound--production--clientsecret"] = "segredo-b-prod";
        AvalaraOutboundSettings bSandbox = h.Settings("tenant-b", clientId: "id-b", baseUrl: "https://avalara-b/");
        AvalaraOutboundSettings bProduction = h.Settings("tenant-b", environment: "Production", clientId: "id-b-prod", baseUrl: "https://avalara-b-prod/");
        await h.Provider.GetTokenAsync(bSandbox);
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(bProduction));
        await h.Provider.GetTokenAsync(h.Settings("tenant-a"));

        h.Provider.Forget("tenant-a");
        AvalaraAccessToken cached = await h.Provider.GetTokenAsync(bSandbox);
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Provider.GetTokenAsync(bProduction));

        Assert.Equal(3, h.Endpoint.Calls);   // nada de novo para o tenant-b
        Assert.False(cached.IsFresh);
    }

    // ---------- ToString ----------

    [Fact]
    public async Task Token_and_resolved_credential_do_not_print_their_values()
    {
        var h = new Harness();
        AvalaraAccessToken token = await h.Provider.GetTokenAsync(h.Settings("tenant-a"));
        var credential = new AvalaraResolvedCredential("tenant-a", "sandbox", new Uri("https://avalara-a/oauth/token"), "id-a", "segredo-a");

        Assert.DoesNotContain(token.Value, token.ToString());
        Assert.Contains("tenant-a", token.ToString());
        Assert.DoesNotContain("segredo-a", credential.ToString());
        Assert.Contains("sandbox", credential.ToString());
    }

    [Fact]
    public void No_token_is_an_explicit_value()
    {
        Assert.True(AvalaraAccessToken.None.IsNone);
        Assert.False(AvalaraAccessToken.None.IsFresh);
    }

    // ---------- apoio ----------

    private static AvalaraOutboundSettings Read(string tenant, string outbound, string environment = "Sandbox")
        => AvalaraOutboundSettings.Read(tenant, new TenantConnectorProfile
        {
            TenantId = tenant,
            Environment = environment,
            Realtime = false,
            InboundAdapter = "Xml",
            OutboundAdapter = "Avalara",
            OutboundSettings = outbound,
        });

    private sealed class Harness
    {
        private AvalaraTokenProvider? _provider;

        public Harness() => Secrets.Values[SecretA] = "segredo-a";

        public TokenEndpointStub Endpoint { get; } = new();

        public FakeSecrets Secrets { get; } = new();

        public FakeClock Clock { get; } = new();

        public CapturingLogger<AvalaraTokenProvider> Logger { get; } = new();

        public AvalaraTokenProvider Provider => _provider ??= new AvalaraTokenProvider(
            new HttpClient(Endpoint), Secrets, Options.Create(new AvalaraOptions()), Clock, Logger);

        public AvalaraOutboundSettings Settings(
            string tenant, string environment = "Sandbox", string? clientId = null, string? baseUrl = null, string? tokenUrl = null)
        {
            string section = environment.ToLowerInvariant();
            string token = tokenUrl is null ? string.Empty : $",\"tokenUrl\":\"{tokenUrl}\"";
            string url = baseUrl ?? $"https://avalara-{tenant[^1]}/";
            string id = clientId ?? $"id-{tenant[^1]}";
            return Read(tenant, $$$"""
                {"{{{section}}}":{"baseUrl":"{{{url}}}","clientId":"{{{id}}}",
                  "clientSecretRef":"kv:fh-{{{tenant}}}--outbound--{{{section}}}--clientsecret"{{{token}}}}}
                """, environment);
        }
    }

    internal sealed class FakeSecrets : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public int GetCount { get; private set; }

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
        {
            GetCount++;
            return Task.FromResult(Values.GetValueOrDefault(name));
        }

        public Task SetAsync(string name, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Relógio controlável — fake manual, sem libs de mock.</summary>
    internal sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    internal sealed record TokenRequest(HttpMethod Method, string Url, string? ContentType, Dictionary<string, string> Form, bool HadAuthorizationHeader)
    {
        public (string Url, string ClientId, string Secret) Summary => (Url, Form["client_id"], Form["client_secret"]);
    }

    /// <summary>Endpoint de token falso: guarda cada pedido e devolve um token distinto a cada um.</summary>
    internal sealed class TokenEndpointStub : HttpMessageHandler
    {
        private readonly List<TokenRequest> _requests = [];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<TokenRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public int? ExpiresIn { get; set; } = 86400;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>Corpo fixo; sem ele, um token novo a cada pedido.</summary>
        public string? Body { get; set; }

        /// <summary>Clientes que o endpoint recusa com 401, qualquer que seja o <see cref="Status"/>.</summary>
        public HashSet<string> RefusedClientIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int n = Interlocked.Increment(ref _calls);
            string raw = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Dictionary<string, string> form = raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : string.Empty));
            lock (_requests)
            {
                _requests.Add(new TokenRequest(request.Method, request.RequestUri!.ToString(), request.Content?.Headers.ContentType?.MediaType,
                    form, request.Headers.Authorization is not null));
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct);
            }

            if (form.TryGetValue("client_id", out string? clientId) && RefusedClientIds.Contains(clientId))
            {
                return Response(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
            }

            string validity = ExpiresIn is { } seconds ? $",\"expires_in\":{seconds}" : string.Empty;
            return Response(Status, Body ?? $$"""{"access_token":"tok-{{n}}","token_type":"Bearer"{{validity}}}""");
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
