using Azure.Core;
using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica os provedores de token do D365: escopo do ambiente, segredo resolvido da referência kv: no cofre de
/// conectores (só se for do próprio tenant), cache da credencial por tenant/app, os três motivos da falta de credencial e
/// a linha de identidade, uma vez por tenant. Credencial e cofre falsos, sem rede.
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
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "s3cr3t")), Logger(), (t, c, s) =>
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
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "1"), ("fh-tenant-a--inbound--auth2--clientsecret", "2")), Logger(), (_, _, _) =>
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
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(), Logger(), (_, _, _) =>
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
        var provider = new ClientCredentialsD365TokenProvider(secrets, Logger(), (_, _, _) =>
        {
            created = true;
            return new FakeCredential(Now);
        });

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(
            () => provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-b--inbound--auth--clientsecret")));
        Assert.Contains("fora do prefixo", ex.Message);   // credencial errada, e não falta de credencial: o motivo de hoje
        Assert.Equal(0, secrets.Reads);   // o cofre nem foi lido
        Assert.False(created);
    }

    [Fact]
    public async Task Secret_reference_without_the_kv_prefix_keeps_its_reason()
    {
        var secrets = new FakeSecrets();
        var provider = new ClientCredentialsD365TokenProvider(secrets, Logger(), (_, _, _) => throw new InvalidOperationException("não pode criar credencial"));

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(Connection("entra-a", "app-a", "s3cr3t")));

        Assert.Contains("kv:<nome>", ex.Message);
        Assert.DoesNotContain("s3cr3t", ex.Message);
        Assert.Equal(0, secrets.Reads);
    }

    // ---------- sem credencial utilizável: três motivos (change explicit-credential-and-execution-cnpj, D2) ----------

    [Fact]
    public async Task Profile_without_auth_says_the_credential_is_not_configured()
    {
        var secrets = new FakeSecrets();
        var provider = new ClientCredentialsD365TokenProvider(secrets, Logger(), (_, _, _) => throw new InvalidOperationException("não pode criar credencial"));

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(new D365Connection("tenant-a", Env, Auth: null)));

        Assert.Equal(
            "A credencial do ERP não está configurada: o perfil não tem Tenant do Entra ID, Client ID nem Client Secret. "
            + "Configure em Configurações → Conectores → Entrada.",
            ex.Message);
        Assert.Equal(0, secrets.Reads);
    }

    [Theory]
    [InlineData(null, "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret", "Tenant do Entra ID")]
    [InlineData("entra-a", "", "kv:fh-tenant-a--inbound--auth--clientsecret", "Client ID")]
    [InlineData("entra-a", "app-a", null, "Client Secret")]
    [InlineData("", " ", "kv:fh-tenant-a--inbound--auth--clientsecret", "Tenant do Entra ID e Client ID")]   // o seed de dev
    [InlineData(null, "app-a", "", "Tenant do Entra ID e Client Secret")]
    [InlineData("entra-a", null, " ", "Client ID e Client Secret")]
    [InlineData(null, null, null, "Tenant do Entra ID, Client ID e Client Secret")]
    public async Task Incomplete_auth_names_only_the_missing_fields_by_their_screen_names(
        string? entraTenant, string? clientId, string? secretRef, string missing)
    {
        var secrets = new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "s3cr3t"));
        var provider = new ClientCredentialsD365TokenProvider(secrets, Logger(), (_, _, _) => throw new InvalidOperationException("não pode criar credencial"));

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(() =>
            provider.GetTokenAsync(new D365Connection("tenant-a", Env, new D365AuthSettings(entraTenant, clientId, secretRef))));

        Assert.Equal($"A credencial do ERP está incompleta: falta {missing}. Configure em Configurações → Conectores → Entrada.", ex.Message);
        Assert.Equal(0, secrets.Reads);
    }

    [Fact]
    public async Task Secret_missing_in_the_vault_says_so()
    {
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(), Logger(), (_, _, _) => throw new InvalidOperationException("não pode criar credencial"));

        var ex = await Assert.ThrowsAsync<ConnectorSettingsException>(() =>
            provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret")));

        Assert.Equal(
            "O Client Secret do ERP do tenant 'tenant-a' não está no cofre. Grave o Client Secret de novo em Configurações → "
            + "Conectores → Entrada.",
            ex.Message);
    }

    [Fact]
    public async Task The_three_reasons_are_distinct()
    {
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(), Logger(), (_, _, _) => throw new InvalidOperationException("não pode criar credencial"));
        D365Connection[] cases =
        [
            new("tenant-a", Env, Auth: null),
            new("tenant-a", Env, new D365AuthSettings("", "", "kv:fh-tenant-a--inbound--auth--clientsecret")),
            Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"),
        ];

        var reasons = new List<string>();
        foreach (D365Connection connection in cases)
        {
            reasons.Add((await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(connection))).Message);
        }

        Assert.Equal(3, reasons.Distinct(StringComparer.Ordinal).Count());
        Assert.All(reasons, r => Assert.Contains("Configurações → Conectores → Entrada", r));
    }

    // ---------- a identidade no log (D3) ----------

    [Fact]
    public async Task The_identity_is_logged_once_per_tenant_without_the_secret()
    {
        var logger = Logger();
        var provider = new ClientCredentialsD365TokenProvider(
            new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "s3cr3t")), logger, (_, _, _) => new FakeCredential(Now.AddHours(1)));

        for (int i = 0; i < 3; i++)
        {
            await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        }

        (LogLevel level, string line) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Equal(
            "D365: o tenant tenant-a autentica no F&O com a credencial do próprio tenant (client credentials: app app-a, tenant do Entra entra-a).",
            line);
        Assert.DoesNotContain("s3cr3t", line);
    }

    [Fact]
    public async Task Each_tenant_gets_its_own_identity_line()
    {
        var logger = Logger();
        var provider = new ClientCredentialsD365TokenProvider(
            new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "a"), ("fh-tenant-b--inbound--auth--clientsecret", "b")),
            logger, (_, _, _) => new FakeCredential(Now.AddHours(1)));

        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        await provider.GetTokenAsync(new D365Connection("tenant-b", Env, new D365AuthSettings("entra-b", "app-b", "kv:fh-tenant-b--inbound--auth--clientsecret")));
        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("tenant tenant-a", logger.Entries[0].Text);
        Assert.Contains("tenant tenant-b", logger.Entries[1].Text);
    }

    [Fact]
    public async Task A_new_client_id_is_a_new_identity_line()
    {
        var logger = Logger();
        var provider = new ClientCredentialsD365TokenProvider(
            new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "s3cr3t")), logger, (_, _, _) => new FakeCredential(Now.AddHours(1)));

        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        await provider.GetTokenAsync(Connection("entra-a", "app-b", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        await provider.GetTokenAsync(Connection("entra-a", "app-b", "kv:fh-tenant-a--inbound--auth--clientsecret"));

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("app app-b", logger.Entries[1].Text);
    }

    [Fact]
    public async Task A_rotated_secret_with_the_same_app_is_not_a_new_identity()
    {
        var logger = Logger();
        var secrets = new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "antigo"));
        int created = 0;
        var provider = new ClientCredentialsD365TokenProvider(secrets, logger, (_, _, _) =>
        {
            created++;
            return new FakeCredential(Now.AddHours(1));
        });

        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));
        secrets.Set("fh-tenant-a--inbound--auth--clientsecret", "novo");
        await provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));

        Assert.Equal(2, created);   // a rotação passou: credencial nova
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task Without_a_usable_credential_no_identity_is_logged()
    {
        var logger = Logger();
        var provider = new ClientCredentialsD365TokenProvider(new FakeSecrets(), logger, (_, _, _) => new FakeCredential(Now.AddHours(1)));

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => provider.GetTokenAsync(new D365Connection("tenant-a", Env, Auth: null)));
        await Assert.ThrowsAsync<ConnectorSettingsException>(() =>
            provider.GetTokenAsync(new D365Connection("tenant-a", Env, new D365AuthSettings("entra-a", null, "kv:fh-tenant-a--inbound--auth--clientsecret"))));
        await Assert.ThrowsAsync<ConnectorSettingsException>(() =>
            provider.GetTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret")));   // cofre vazio

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task The_credential_test_token_does_not_log_the_identity()
    {
        var logger = Logger();
        var provider = new ClientCredentialsD365TokenProvider(
            new FakeSecrets(("fh-tenant-a--inbound--auth--clientsecret", "s3cr3t")), logger, (_, _, _) => new FakeCredential(Now.AddHours(1)));

        await provider.GetFreshTokenAsync(Connection("entra-a", "app-a", "kv:fh-tenant-a--inbound--auth--clientsecret"));

        Assert.Empty(logger.Entries);
    }

    private static D365Connection Connection(string entraTenant, string clientId, string secretRef)
        => new("tenant-a", Env, new D365AuthSettings(entraTenant, clientId, secretRef));

    private static D365ChangeFeedTests.ListLogger<ClientCredentialsD365TokenProvider> Logger() => new();

    /// <summary>Um cofre em memória, que conta as leituras.</summary>
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
}
