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
        D365AuthSettings auth = connection.Auth
            ?? throw new ConnectorSettingsException("Settings do tenant sem auth: o client credentials precisa de tenantId, clientId e clientSecretRef.");
        (string entraTenant, string clientId, string secretRef) = auth.RequireComplete();
        string secret = await ResolveAsync(connection.TenantId, secretRef, ct);

        // A impressão do segredo entra na chave: uma rotação no cofre gera credencial nova, sem guardar o segredo em claro como chave.
        string key = $"{entraTenant}|{clientId}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))}";
        TokenCredential credential = _credentials.GetOrAdd(key, _ => _createCredential(entraTenant, clientId, secret));

        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([connection.Scope]), ct);
        return token.Token;
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
