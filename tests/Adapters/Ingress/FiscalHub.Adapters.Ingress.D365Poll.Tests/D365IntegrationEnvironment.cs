using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// O que os testes contra o F&amp;O real pedem do ambiente (change explicit-credential-and-execution-cnpj, D8): a URL do
/// ambiente e a credencial do app do conector, cada uma numa variável. A autenticação é a do conector, client credentials,
/// e nunca outra identidade da máquina. Os nomes levam o prefixo do hub, e não o <c>AZURE_*</c>, que o
/// <c>EnvironmentCredential</c> de qualquer processo leria. As variáveis ficam na sessão (<c>$env:</c>), nunca em arquivo
/// versionado.
/// </summary>
internal static class D365IntegrationEnvironment
{
    public const string Url = "FISCALHUB_D365_URL";
    public const string EntraTenantId = "FISCALHUB_D365_ENTRA_TENANT_ID";
    public const string ClientId = "FISCALHUB_D365_CLIENT_ID";
    public const string ClientSecret = "FISCALHUB_D365_CLIENT_SECRET";

    /// <summary>O nome do segredo no cofre em memória: o mesmo que a tela grava para o tenant-a.</summary>
    public const string SecretName = "fh-tenant-a--inbound--auth--clientsecret";

    private static readonly string[] Required = [Url, EntraTenantId, ClientId, ClientSecret];

    /// <summary>As variáveis que faltam, na ordem de <see cref="Required"/>. O valor em branco conta como falta.</summary>
    public static IReadOnlyList<string> Missing(Func<string, string?> read)
        => [.. Required.Where(name => string.IsNullOrWhiteSpace(read(name)))];

    /// <summary>O motivo do skip, nomeando o que falta; <c>null</c> quando nada falta.</summary>
    public static string? SkipReason(Func<string, string?> read)
    {
        IReadOnlyList<string> missing = Missing(read);
        return missing.Count == 0
            ? null
            : $"Integração com F&O real: falta {List(missing)}. Defina {List(Required)} (o app do conector) para rodar.";
    }

    /// <summary>O provider de produção, com o segredo da variável num cofre em memória, sob o nome do tenant-a.</summary>
    public static ClientCredentialsD365TokenProvider Tokens()
        => new(new OneSecret(SecretName, Read(ClientSecret)), NullLogger<ClientCredentialsD365TokenProvider>.Instance);

    /// <summary>A seção <c>auth</c> das settings, com a referência <c>kv:</c>, como a tela grava.</summary>
    public static string AuthJson()
        => $$"""{"tenantId":"{{Read(EntraTenantId)}}","clientId":"{{Read(ClientId)}}","clientSecretRef":"kv:{{SecretName}}"}""";

    public static string Read(string name) => Environment.GetEnvironmentVariable(name)!;

    private static string List(IReadOnlyList<string> names)
        => names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}";

    private sealed class OneSecret(string name, string value) : ISecretStore
    {
        public Task<string?> GetAsync(string secretName, CancellationToken ct = default)
            => Task.FromResult<string?>(secretName == name ? value : null);

        public Task SetAsync(string secretName, string secretValue, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SecretDescription?> DescribeAsync(string secretName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
