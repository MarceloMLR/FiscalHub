using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Provedor de token com cache por tenant: obtém o token via OAuth client credentials, reusa
/// enquanto válido e renova com margem de segurança. Sob concorrência, apenas uma busca por
/// tenant — as demais chamadas aguardam e reaproveitam o resultado.
/// </summary>
internal sealed class AvalaraTokenProvider : IAvalaraTokenProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AvalaraOptions _options;
    private readonly TimeProvider _clock;

    // Cache e trava são POR TENANT: um tenant buscando token não bloqueia os demais.
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public AvalaraTokenProvider(HttpClient http, IOptions<AvalaraOptions> options, TimeProvider clock)
    {
        _http = http;
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc/>
    public async Task<string> GetTokenAsync(string tenantId, CancellationToken ct = default)
    {
        // 1. Caminho rápido: token válido em cache — nem encosta na trava.
        if (TryGetValid(tenantId, out string? cached))
        {
            return cached;
        }

        SemaphoreSlim gate = _gates.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // 2. Dupla verificação: enquanto esperávamos na fila, outra chamada pode já ter
            //    preenchido o cache. Sem isto, todas buscariam — só que em fila, o que é pior.
            if (TryGetValid(tenantId, out cached))
            {
                return cached;
            }

            // 3. Só quem chegou primeiro busca de fato.
            CachedToken fresh = await FetchAsync(ct);
            _cache[tenantId] = fresh;
            return fresh.Value;
        }
        finally
        {
            // 4. Liberar SEMPRE: uma exceção sem release travaria esse tenant para sempre.
            gate.Release();
        }
    }

    private bool TryGetValid(string tenantId, [NotNullWhen(true)] out string? token)
    {
        token = null;

        // A margem antecipa a renovação, evitando usar um token que vence no meio da requisição.
        if (_cache.TryGetValue(tenantId, out CachedToken? entry)
            && entry.ExpiresAt - _options.TokenRenewalMargin > _clock.GetUtcNow())
        {
            token = entry.Value;
            return true;
        }

        return false;
    }

    private async Task<CachedToken> FetchAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            }),
        };

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        TokenResponse body = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Resposta vazia do endpoint de token.");

        string value = body.AccessToken
            ?? throw new InvalidOperationException("Resposta do endpoint de token sem access_token.");

        return new CachedToken(value, _clock.GetUtcNow().AddSeconds(body.ExpiresIn));
    }

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    // Resposta nativa do OAuth (snake_case) — presa ao adapter.
    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
