using System.Collections.Concurrent;
using Azure;
using Azure.Security.KeyVault.Secrets;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Infrastructure.Secrets;

/// <summary>
/// O cofre dos segredos de conector sobre o <see cref="SecretClient"/> oficial (ADR-0027). É o único adapter: em
/// produção fala com o Key Vault; em dev, com um emulador da mesma API, em memória. Só muda a configuração do cliente.
/// <list type="bullet">
/// <item>Só aceita nomes de conector (<see cref="SecretNames.Prefix"/>), antes de chamar o cofre. A política de acesso
/// do cofre de produção é quem vale contra um host comprometido; esta guarda faz um defeito nosso aparecer em teste.</item>
/// <item>O valor lido fica em cache por pouco tempo, para o envio não ler o cofre a cada nota, e a gravação invalida na
/// hora. A rotação feita direto no cofre, fora da tela, é vista no fim do intervalo.</item>
/// <item>Nenhuma mensagem de erro leva o valor: o SDK não loga corpo, e a falha cita só o nome e o status.</item>
/// </list>
/// </summary>
internal sealed class KeyVaultSecretStore : ISecretStore
{
    private static readonly TimeSpan DefaultCacheFor = TimeSpan.FromMinutes(5);

    private readonly SecretClient _client;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _cacheFor;
    private readonly ConcurrentDictionary<string, CachedValue> _cache = new(StringComparer.Ordinal);

    public KeyVaultSecretStore(SecretClient client, TimeProvider clock, TimeSpan? cacheFor = null)
    {
        _client = client;
        _clock = clock;
        _cacheFor = cacheFor ?? DefaultCacheFor;
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        Guard(name);
        if (_cache.TryGetValue(name, out CachedValue? hit) && hit.Until > _clock.GetUtcNow())
        {
            return hit.Value;
        }

        try
        {
            // A versão explícita fixa a sobrecarga de três parâmetros; sem ela, o SDK 4.11 escolhe a nova, com outContentType.
            Response<KeyVaultSecret> response = await _client.GetSecretAsync(name, version: null, cancellationToken: ct);
            string? value = response.Value.Value;
            if (string.IsNullOrEmpty(value))
            {
                _cache.TryRemove(name, out _);
                return null;
            }

            _cache[name] = new CachedValue(value, _clock.GetUtcNow() + _cacheFor);
            return value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.TryRemove(name, out _);
            return null;
        }
        catch (RequestFailedException ex)
        {
            throw Failure("ler", name, ex);
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        Guard(name);
        try
        {
            await _client.SetSecretAsync(name, value, ct);
        }
        catch (RequestFailedException ex)
        {
            throw Failure("gravar", name, ex);
        }

        // A gravação pela tela vale na hora: a próxima leitura vai ao cofre.
        _cache.TryRemove(name, out _);
    }

    public async Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default)
    {
        Guard(name);
        try
        {
            // Só os metadados das versões: o valor nunca é lido para dizer "configurado".
            SecretProperties? current = null;
            await foreach (SecretProperties version in _client.GetPropertiesOfSecretVersionsAsync(name, ct))
            {
                if (version.Enabled != false && (current is null || version.UpdatedOn > current.UpdatedOn))
                {
                    current = version;
                }
            }

            return current is null ? null : new SecretDescription(current.UpdatedOn);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException ex)
        {
            throw Failure("descrever", name, ex);
        }
    }

    private static void Guard(string name)
    {
        if (!SecretNames.IsConnectorSecret(name) || !SecretReference.IsValidName(name))
        {
            throw new ArgumentException(
                $"O cofre só guarda segredos de conector, com o prefixo '{SecretNames.Prefix}': '{name}' não é um deles.", nameof(name));
        }
    }

    // A exceção do SDK entra como interna: traz o status e o erro do cofre, e nunca o corpo do pedido (o valor).
    private static InvalidOperationException Failure(string operation, string name, RequestFailedException ex)
        => new($"O cofre não conseguiu {operation} o segredo '{name}' (HTTP {ex.Status}).", ex);

    private sealed record CachedValue(string Value, DateTimeOffset Until);
}
