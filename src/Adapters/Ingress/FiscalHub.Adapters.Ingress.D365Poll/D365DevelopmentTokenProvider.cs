using System.Collections.Concurrent;
using FiscalHub.Application.Connectors;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Token do D365 em DESENVOLVIMENTO: o perfil com auth completo (<c>tenantId</c>, <c>clientId</c> e a referência do
/// segredo) e o segredo presente no cofre usa a credencial do próprio tenant, como em produção; sem isso, cai na sessão
/// do Azure CLI (<c>az login</c>). Assim, preencher o Client ID e o Client Secret na tela muda a identidade de verdade.
/// <para>A referência fora do prefixo do tenant não cai no Azure CLI: é credencial errada, e não falta de credencial, e
/// continua o erro de configuração do client credentials.</para>
/// <para>Uma linha de log por tenant diz qual identidade autenticou, e sai de novo quando ela muda. Só entra no DI pelo
/// <c>UseD365AzureCliFallback()</c>, que o host chama só em Development; em produção, o Azure CLI nunca entra.</para>
/// </summary>
internal sealed class D365DevelopmentTokenProvider : ID365TokenProvider
{
    private readonly ID365TokenProvider _tenantCredential;
    private readonly ID365TokenProvider _azureCli;
    private readonly ISecretStore _secrets;
    private readonly ILogger<D365DevelopmentTokenProvider> _logger;
    private readonly ConcurrentDictionary<string, string> _lastIdentity = new(StringComparer.Ordinal);

    public D365DevelopmentTokenProvider(
        ID365TokenProvider tenantCredential, ID365TokenProvider azureCli, ISecretStore secrets, ILogger<D365DevelopmentTokenProvider> logger)
    {
        _tenantCredential = tenantCredential;
        _azureCli = azureCli;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
    {
        D365AuthSettings? auth = connection.Auth;
        string[] missing = auth is null
            ? []
            : [.. new[] { ("tenantId", auth.TenantId), ("clientId", auth.ClientId), ("clientSecretRef", auth.ClientSecretRef) }
                .Where(f => string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1)];

        string reason;
        if (auth is null)
        {
            reason = "o perfil não tem auth";
        }
        else if (missing.Length > 0)
        {
            reason = $"auth incompleto no perfil: falta {string.Join(", ", missing)}";
        }
        else if (!SecretReference.TryParse(auth.ClientSecretRef, out string? name) || !SecretNames.BelongsTo(name, connection.TenantId))
        {
            // Credencial errada, e não ausente: o client credentials recusa com o motivo, e o Azure CLI não entra.
            return await _tenantCredential.GetTokenAsync(connection, ct);
        }
        else if (string.IsNullOrEmpty(await _secrets.GetAsync(name, ct)))
        {
            reason = "o cofre não tem o segredo de auth.clientSecretRef";
        }
        else
        {
            Report(connection.TenantId,
                $"a credencial do próprio tenant (client credentials: app {auth.ClientId}, tenant do Entra {auth.TenantId})");
            return await _tenantCredential.GetTokenAsync(connection, ct);
        }

        Report(connection.TenantId, $"o Azure CLI (a identidade delegada do az login), porque {reason}");
        return await _azureCli.GetTokenAsync(connection, ct);
    }

    // Uma vez por tenant, e de novo quando a identidade muda (o Admin gravou o segredo, o emulador reiniciou…).
    private void Report(string tenantId, string identity)
    {
        if (_lastIdentity.TryGetValue(tenantId, out string? last) && last == identity)
        {
            return;
        }

        _lastIdentity[tenantId] = identity;
        _logger.LogInformation("D365: o tenant {Tenant} autentica no F&O com {Identity}.", tenantId, identity);
    }
}
