using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Azure.Identity;
using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Configuration;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Token de produção: client credentials no Entra ID, com tenant do Entra, app e segredo por referência
/// nas settings do tenant. Uma credencial por (tenant do Entra, app, segredo); o Azure.Identity guarda o
/// token em memória por instância e renova antes de vencer — sem OAuth escrito à mão.
/// </summary>
internal sealed class ClientCredentialsD365TokenProvider : ID365TokenProvider
{
    private readonly IConfiguration _configuration;
    private readonly Func<string, string, string, TokenCredential> _createCredential;
    private readonly ConcurrentDictionary<string, TokenCredential> _credentials = new();

    public ClientCredentialsD365TokenProvider(
        IConfiguration configuration,
        Func<string, string, string, TokenCredential>? createCredential = null)
    {
        _configuration = configuration;
        _createCredential = createCredential ?? ((tenant, client, secret) => new ClientSecretCredential(tenant, client, secret));
    }

    public async Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
    {
        D365AuthSettings auth = connection.Auth
            ?? throw new ConnectorSettingsException("Settings do tenant sem auth: o client credentials precisa de tenantId, clientId e clientSecretRef.");
        (string entraTenant, string clientId, string secretRef) = auth.RequireComplete();
        string secret = SecretReference.Resolve(secretRef, _configuration);

        // A impressão do segredo entra na chave: uma rotação no Key Vault gera credencial nova, sem guardar o segredo em claro como chave.
        string key = $"{entraTenant}|{clientId}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))}";
        TokenCredential credential = _credentials.GetOrAdd(key, _ => _createCredential(entraTenant, clientId, secret));

        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([connection.Scope]), ct);
        return token.Token;
    }
}
