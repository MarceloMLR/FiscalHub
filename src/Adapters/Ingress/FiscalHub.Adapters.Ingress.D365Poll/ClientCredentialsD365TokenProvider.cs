using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Azure.Identity;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Token de produção: client credentials no Entra ID, com tenant do Entra, app e segredo por referência
/// nas settings do tenant. O segredo vem do cofre de conectores (ADR-0027), pela referência — e só se ela for do
/// próprio tenant. Uma credencial por (tenant do Entra, app, segredo); o Azure.Identity guarda o token em memória por
/// instância e renova antes de vencer — sem OAuth escrito à mão.
/// </summary>
internal sealed class ClientCredentialsD365TokenProvider : ID365TokenProvider
{
    private readonly ISecretStore _secrets;
    private readonly Func<string, string, string, TokenCredential> _createCredential;
    private readonly ConcurrentDictionary<string, TokenCredential> _credentials = new();

    public ClientCredentialsD365TokenProvider(
        ISecretStore secrets,
        Func<string, string, string, TokenCredential>? createCredential = null)
    {
        _secrets = secrets;
        _createCredential = createCredential ?? ((tenant, client, secret) => new ClientSecretCredential(tenant, client, secret));
    }

    public async Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
    {
        (string entraTenant, string clientId, string secret) = await ResolveCredentialAsync(connection, ct);

        // A impressão do segredo entra na chave: uma rotação no cofre gera credencial nova, sem guardar o segredo em claro como chave.
        string key = $"{entraTenant}|{clientId}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))}";
        TokenCredential credential = _credentials.GetOrAdd(key, _ => _createCredential(entraTenant, clientId, secret));

        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([connection.Scope]), ct);
        return token.Token;
    }

    /// <summary>
    /// Um token NOVO do Entra ID, para o teste de credencial (change module-navigation-and-integration-panel, D11). A
    /// resolução da referência e do cofre é a mesma do coletor, mas a credencial é uma instância nova por chamada, que não
    /// entra no cache de credenciais. O Azure.Identity guarda o token na instância: uma nova começa sem cache e vai ao
    /// Entra ID. Um teste que reusasse o token do coletor passaria com um segredo já revogado. O <see cref="GetTokenAsync"/>
    /// do coletor não muda.
    /// </summary>
    public async Task<string> GetFreshTokenAsync(D365Connection connection, CancellationToken ct = default)
    {
        (string entraTenant, string clientId, string secret) = await ResolveCredentialAsync(connection, ct);
        TokenCredential credential = _createCredential(entraTenant, clientId, secret);
        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([connection.Scope]), ct);
        return token.Token;
    }

    // O auth completo e o segredo do cofre, pela referência do próprio tenant: a regra mora aqui, para o coletor e o teste.
    private async Task<(string EntraTenant, string ClientId, string Secret)> ResolveCredentialAsync(D365Connection connection, CancellationToken ct)
    {
        D365AuthSettings auth = connection.Auth
            ?? throw new ConnectorSettingsException("Settings do tenant sem auth: o client credentials precisa de tenantId, clientId e clientSecretRef.");
        (string entraTenant, string clientId, string secretRef) = auth.RequireComplete();
        return (entraTenant, clientId, await ResolveAsync(connection.TenantId, secretRef, ct));
    }

    // A referência tem de ser do próprio tenant: senão um tenant usaria a credencial de outro (ADR-0027).
    private async Task<string> ResolveAsync(string tenantId, string secretRef, CancellationToken ct)
    {
        if (!SecretReference.TryParse(secretRef, out string? name))
        {
            throw new ConnectorSettingsException("Segredo precisa vir por referência \"kv:<nome>\".");
        }

        if (!SecretNames.BelongsTo(name, tenantId))
        {
            throw new ConnectorSettingsException(
                $"auth.clientSecretRef do tenant '{tenantId}' aponta para um segredo fora do prefixo dele ({SecretNames.TenantPrefix(tenantId)}).");
        }

        return await _secrets.GetAsync(name, ct)
            ?? throw new ConnectorSettingsException(
                $"O segredo do client credentials do tenant '{tenantId}' não está no cofre. Configure em Configurações → Conectores → entrada.");
    }
}
