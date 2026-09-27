using Azure.Core;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica os provedores de token do D365: escopo do ambiente, segredo resolvido da referência kv: no cofre de
/// conectores (só se for do próprio tenant), cache da credencial por tenant/app e cache do token do Azure CLI.
/// Credencial e cofre falsos, sem rede.
/// </summary>
public class D365TokenProviderTests
{
    private static readonly Uri Env = new("https://fiscosysdev.operations.dynamics.com");
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Client_credentials_uses_the_resolved_secret_and_the_environment_scope()
    {
        var created = new List<(string Tenant, string Client, string Secret)>();
        var credential = new FakeCredential(Now.AddHours(1));
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "s3cr3t")), (t, c, s) =>
        {
            created.Add((t, c, s));
            return credential;
        });

        string token = await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));

        Assert.Equal("token-1", token);
        Assert.Equal([("entra-a", "app-a", "s3cr3t")], created);
        Assert.Equal(["https://fiscosysdev.operations.dynamics.com/.default"], credential.Scopes.Single());
    }

    [Fact]
    public async Task Client_credentials_reuses_the_credential_per_tenant_and_app()
    {
        int created = 0;
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "1"), ("fh-tenant-a--inbound--auth2--clientsecret", "2")), (_, _, _) =>
        {
            created++;
            return new FakeCredential(Now.AddHours(1));
        });

        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        Assert.Equal(1, created);

        await provider.GetTokenAsync(Connection("entra-b", "app-b", "kv:fh-tenant-a--inbound--auth2--clientsecret"));
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task Unresolved_secret_reference_is_a_configuration_error()
    {
        bool created = false;
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(), (_, _, _) =>
        {
            created = true;
            return new FakeCredential(Now);
        });

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--nao-existe")));
        Assert.False(created);
    }

    [Fact]
    public async Task Secret_reference_of_another_tenant_is_refused_without_reading_the_vault()
    {
        var secrets = new FakeSecrets(("fh-tenant-b--inbound--auth--clientsecret", "do-tenant-b"));
        bool created = false;
        var provider = new ClientCredentialsD365TokenProvider(secrets, (_, _, _) =>
        {
            created = true;
            return new FakeCredential(Now);
        });

        await Assert.ThrowsAsync<ConnectorSettingsException>(
            () => provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-b--inbound--auth--clientsecret")));
        Assert.Equal(0, secrets.Reads);   // o cofre nem foi lido
        Assert.False(created);
    }

    [Fact]
    public async Task Missing_or_incomplete_auth_is_a_configuration_error()
    {
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(), (_, _, _) => new FakeCredential(Now));

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(new D365Connection("tenant-a", Env, Auth: null)));
        await Assert.ThrowsAsync<ConnectorSettingsException>(() =>
            provider.GetTokenAsync(new D365Connection("tenant-a", Env, new D365AuthSettings("entra-a", null, "kv:x"))));
    }

    [Fact]
    public async Task Azure_cli_token_is_cached_until_close_to_expiry()
    {
        var clock = new FakeClock(Now);
        var credential = new FakeCredential(Now.AddMinutes(60));
        var provider = new AzureCliD365TokenProvider(clock, credential);
        D365Connection connection = new("tenant-a", Env, Auth: null);   // dev não precisa de auth

        Assert.Equal("token-1", await provider.GetTokenAsync(connection));
        clock.Now = Now.AddMinutes(50);
        Assert.Equal("token-1", await provider.GetTokenAsync(connection));   // ainda longe de vencer

        clock.Now = Now.AddMinutes(56);   // dentro da margem de 5 min
        Assert.Equal("token-2", await provider.GetTokenAsync(connection));
        Assert.All(credential.Scopes, s => Assert.Equal(["https://fiscosysdev.operations.dynamics.com/.default"], s));
    }

    private static D365Connection Connection(string entraTenant, string clientId, string secretRef)
        => new("tenant-a", Env, new D365AuthSettings(entraTenant, clientId, secretRef));

    /// <summary>Um cofre em memória, que conta as leituras.</summary>
    private sealed class FakeSecrets(params (string Name, string Value)[] values) : ISecretStore
    {
        private readonly Dictionary<string, string> _values = values.ToDictionary(v => v.Name, v => v.Value);

        public int Reads { get; private set; }

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(_values.TryGetValue(name, out string? value) ? value : null);
        }

        public Task SetAsync(string name, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCredential(DateTimeOffset expiresOn) : TokenCredential
    {
        private int _issued;

        public List<string[]> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.Add(requestContext.Scopes);
            _issued++;
            return new AccessToken($"token-{_issued}", expiresOn.AddMinutes(60 * (_issued - 1)));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
