using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Token de DESENVOLVIMENTO: a sessão do Azure CLI do desenvolvedor (<c>az login</c>), para rodar local
/// antes de a app registration existir. Só entra no DI quando o host chama <c>UseD365AzureCliToken()</c>,
/// e o host só chama em Development. Cada busca abre um processo do <c>az</c>, então o token fica em cache
/// por escopo até perto de vencer.
/// </summary>
internal sealed class AzureCliD365TokenProvider : ID365TokenProvider
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _clock;
    private readonly TokenCredential _credential;
    private readonly ConcurrentDictionary<string, AccessToken> _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AzureCliD365TokenProvider(TimeProvider clock, TokenCredential? credential = null)
    {
        _clock = clock;
        _credential = credential ?? new AzureCliCredential();
    }

    public async Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
    {
        if (TryGetValid(connection.Scope, out string? cached))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Dupla verificação: quem esperou na fila reaproveita o token que o primeiro buscou.
            if (TryGetValid(connection.Scope, out cached))
            {
                return cached;
            }

            AccessToken token = await _credential.GetTokenAsync(new TokenRequestContext([connection.Scope]), ct);
            _cache[connection.Scope] = token;
            return token.Token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetValid(string scope, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? token)
    {
        token = null;
        if (_cache.TryGetValue(scope, out AccessToken entry) && entry.ExpiresOn - RenewalMargin > _clock.GetUtcNow())
        {
            token = entry.Token;
            return true;
        }

        return false;
    }
}
