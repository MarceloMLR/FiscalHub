using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica o token do D365 em desenvolvimento: o perfil com auth completo e o segredo no cofre usa a credencial do
/// próprio tenant; sem isso, cai na sessão do Azure CLI. Uma linha de log por tenant diz qual identidade autenticou (e de
/// novo, se ela mudar) — é a pergunta que o teste manual existe para responder.
/// </summary>
public class D365DevelopmentTokenProviderTests
{
    private const string SecretName = "fh-tenant-a--inbound--auth--clientsecret";
    private static readonly Uri Env = new("https://fiscosysdev.operations.dynamics.com");

    [Fact]
    public async Task Complete_auth_with_the_secret_in_the_vault_uses_the_tenant_credential()
    {
        var h = new Harness((SecretName, "s3cr3t"));

        string token = await h.Provider.GetTokenAsync(Complete("tenant-a"));

        Assert.Equal("do-tenant", token);
        Assert.Equal(1, h.TenantCredential.Calls);
        Assert.Equal(0, h.AzureCli.Calls);
        string line = Assert.Single(h.Logger.Entries);
        Assert.Contains("tenant-a", line);
        Assert.Contains("credencial do próprio tenant", line);
        Assert.Contains("app-a", line);
        Assert.DoesNotContain("s3cr3t", line);
    }

    [Theory]
    [InlineData("sem-auth", "o perfil não tem auth")]
    [InlineData("incompleto", "falta tenantId, clientId")]
    [InlineData("cofre-vazio", "o cofre não tem o segredo de auth.clientSecretRef")]
    public async Task Without_a_usable_tenant_credential_it_falls_back_to_the_azure_cli(string situation, string reason)
    {
        var h = new Harness();
        D365Connection connection = situation switch
        {
            "sem-auth" => new D365Connection("tenant-a", Env, Auth: null),
            "incompleto" => new D365Connection("tenant-a", Env, new D365AuthSettings("", "", $"kv:{SecretName}")),   // o seed
            _ => Complete("tenant-a"),
        };

        string token = await h.Provider.GetTokenAsync(connection);

        Assert.Equal("do-az-login", token);
        Assert.Equal(0, h.TenantCredential.Calls);
        string line = Assert.Single(h.Logger.Entries);
        Assert.Contains("Azure CLI", line);
        Assert.Contains(reason, line);
    }

    [Fact]
    public async Task A_reference_outside_the_tenant_prefix_stays_a_configuration_error_and_never_falls_back()
    {
        // Auth completo, mas apontando para o segredo de outro tenant: é credencial errada, e não falta de credencial.
        var secrets = new FakeSecrets(("fh-tenant-b--inbound--auth--clientsecret", "do-tenant-b"));
        var cli = new FakeProvider("do-az-login");
        var provider = new D365DevelopmentTokenProvider(
            new ClientCredentialsD365TokenProvider(secrets, (_, _, _) => throw new InvalidOperationException("não pode criar credencial")),
            cli, secrets, new EntriesLogger<D365DevelopmentTokenProvider>());

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(
            new D365Connection("tenant-a", Env, new D365AuthSettings("entra-a", "app-a", "kv:fh-tenant-b--inbound--auth--clientsecret"))));

        Assert.Equal(0, cli.Calls);
        Assert.Equal(0, secrets.Reads);   // o segredo de outro tenant nem é lido
    }

    [Fact]
    public async Task The_identity_is_logged_once_per_tenant_and_again_when_it_changes()
    {
        var h = new Harness();

        await h.Provider.GetTokenAsync(Complete("tenant-a"));   // cofre vazio: Azure CLI
        await h.Provider.GetTokenAsync(Complete("tenant-a"));
        await h.Provider.GetTokenAsync(new D365Connection("tenant-b", Env, Auth: null));
        h.Secrets.Set(SecretName, "s3cr3t");                    // o Admin grava o segredo na tela
        await h.Provider.GetTokenAsync(Complete("tenant-a"));
        await h.Provider.GetTokenAsync(Complete("tenant-a"));

        Assert.Equal(3, h.Logger.Entries.Count);
        Assert.Contains("Azure CLI", h.Logger.Entries[0]);
        Assert.Contains("tenant-b", h.Logger.Entries[1]);
        Assert.Contains("credencial do próprio tenant", h.Logger.Entries[2]);
    }

    private static D365Connection Complete(string tenant)
        => new(tenant, Env, new D365AuthSettings("entra-a", "app-a", $"kv:fh-{tenant}--inbound--auth--clientsecret"));

    private sealed class Harness
    {
        public Harness(params (string Name, string Value)[] secrets)
        {
            Secrets = new FakeSecrets(secrets);
            Provider = new D365DevelopmentTokenProvider(TenantCredential, AzureCli, Secrets, Logger);
        }

        public FakeProvider TenantCredential { get; } = new("do-tenant");

        public FakeProvider AzureCli { get; } = new("do-az-login");

        public FakeSecrets Secrets { get; }

        public EntriesLogger<D365DevelopmentTokenProvider> Logger { get; } = new();

        public D365DevelopmentTokenProvider Provider { get; }
    }

    private sealed class FakeProvider(string token) : ID365TokenProvider
    {
        public int Calls { get; private set; }

        public Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(token);
        }
    }

    private sealed class FakeSecrets(params (string Name, string Value)[] values) : ISecretStore
    {
        private readonly Dictionary<string, string> _values = values.ToDictionary(v => v.Name, v => v.Value);

        public int Reads { get; private set; }

        public void Set(string name, string value) => _values[name] = value;

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(_values.TryGetValue(name, out string? value) ? value : null);
        }

        public Task SetAsync(string name, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class EntriesLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception));
    }
}
