using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// A credencial resolvida: o endpoint, o cliente e o VALOR do segredo, lido do cofre. Não é <c>record</c> de propósito —
/// o <see cref="ToString"/> traz só o tenant e o ambiente.
/// </summary>
internal sealed class AvalaraResolvedCredential(string tenantId, string environment, Uri tokenEndpoint, string clientId, string secret)
{
    public string TenantId { get; } = tenantId;

    public string Environment { get; } = environment;

    public Uri TokenEndpoint { get; } = tokenEndpoint;

    public string ClientId { get; } = clientId;

    public string Secret { get; } = secret;

    public override string ToString() => $"AvalaraResolvedCredential {{ Tenant = {TenantId}, Environment = {Environment} }}";
}

/// <summary>
/// Token por credencial (ADR-0027). A credencial vem da seção do ambiente ativo, e o segredo, do cofre de conectores.
/// <list type="bullet">
///   <item><b>Cache:</b> por tenant, ambiente, endpoint, cliente e a impressão SHA-256 do segredo — a rotação gera
///   entrada nova sem guardar o segredo como chave. Reusa enquanto vale além da margem, e renova dentro dela.</item>
///   <item><b>Concorrência:</b> uma única busca por credencial; as demais chamadas aguardam e reaproveitam.</item>
///   <item><b>Recusa lembrada:</b> a recusa da credencial fica guardada por <see cref="AvalaraOptions.CredentialRefusalHold"/>,
///   para que cada nota em voo não seja uma tentativa de login com a credencial errada. <see cref="Forget"/> a
///   esquece na hora.</item>
/// </list>
/// Nenhuma mensagem, log ou <c>ToString</c> leva o token, o segredo ou a chave.
/// </summary>
internal sealed class AvalaraTokenProvider : IAvalaraTokenProvider
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly AvalaraOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AvalaraTokenProvider> _logger;

    // Tudo POR CREDENCIAL: um tenant buscando token não bloqueia os demais, e dois tenants nunca dividem uma entrada.
    private readonly ConcurrentDictionary<CacheKey, CachedToken> _cache = new();
    private readonly ConcurrentDictionary<CacheKey, Refusal> _refusals = new();
    private readonly ConcurrentDictionary<CacheKey, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<CacheKey, byte> _warnedWithoutValidity = new();

    public AvalaraTokenProvider(
        HttpClient http, ISecretStore secrets, IOptions<AvalaraOptions> options, TimeProvider clock, ILogger<AvalaraTokenProvider> logger)
    {
        _http = http;
        _secrets = secrets;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
    {
        AvalaraResolvedCredential credential = await ResolveAsync(settings, ct);
        CacheKey key = CacheKey.Of(credential);

        // 1. Caminho rápido: recusa lembrada ou token válido — nem encosta na trava.
        if (TryCached(key, out AvalaraAccessToken? cached))
        {
            return cached;
        }

        SemaphoreSlim gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // 2. Dupla verificação: enquanto esperávamos, outra chamada pode já ter buscado (ou sido recusada).
            if (TryCached(key, out cached))
            {
                return cached;
            }

            // 3. Só quem chegou primeiro busca de fato.
            return await FetchAsync(credential, key, ct);
        }
        finally
        {
            // 4. Liberar SEMPRE: uma exceção sem release travaria essa credencial para sempre.
            gate.Release();
        }
    }

    public void Invalidate(AvalaraAccessToken token)
    {
        if (token.Key is CacheKey key && _cache.TryGetValue(key, out CachedToken? entry) && entry.Value == token.Value)
        {
            _cache.TryRemove(new KeyValuePair<CacheKey, CachedToken>(key, entry));
        }
    }

    public void Forget(string tenantId)
    {
        foreach (CacheKey key in _cache.Keys.Concat(_refusals.Keys).Concat(_warnedWithoutValidity.Keys).Where(k => k.TenantId == tenantId))
        {
            _cache.TryRemove(key, out _);
            _refusals.TryRemove(key, out _);
            _warnedWithoutValidity.TryRemove(key, out _);
        }
    }

    public async Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
    {
        string label = EnvironmentLabel(settings.Environment);
        AvalaraResolvedCredential credential;
        try
        {
            credential = await ResolveAsync(settings, ct);
        }
        catch (DispatchRejectedException ex)
        {
            // Sem URL, sem Client ID, sem o segredo no cofre: a credencial não está completa, e nenhuma requisição saiu.
            return new CredentialTestOutcome(CredentialTestVerdict.Incomplete, ex.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CredentialTestOutcome(CredentialTestVerdict.Unavailable,
                "O cofre de segredos não respondeu agora, e o teste não chegou à plataforma. Tente de novo em instantes.");
        }

        CacheKey key = CacheKey.Of(credential);
        SemaphoreSlim gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Sem o TryCached: nem o token em cache nem a recusa lembrada respondem pelo teste.
            await FetchAsync(credential, key, ct);

            // Funcionou: a recusa e os tokens do tenant são esquecidos, como no salvar do perfil. O token novo fica.
            _cache.TryGetValue(key, out CachedToken? fresh);
            Forget(credential.TenantId);
            if (fresh is not null)
            {
                _cache[key] = fresh;
            }

            return new CredentialTestOutcome(CredentialTestVerdict.Worked, $"A plataforma emitiu um token novo para a credencial de {label}.");
        }
        catch (DispatchRejectedException ex)
        {
            // A recusa (400/401) já ficou lembrada para o envio pelo FetchAsync; o motivo já vem redigido.
            return new CredentialTestOutcome(CredentialTestVerdict.Refused, ex.Reason);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } code)
        {
            // 400 e 401 já viraram recusa no FetchAsync. Dos outros, só 408, 429 e 5xx são indisponibilidade; um 404 ou 403
            // é endereço ou ambiente errado.
            int status = (int)code;
            return status is 408 or 429 || status >= 500
                ? new CredentialTestOutcome(CredentialTestVerdict.Unavailable,
                    $"O endpoint de token de {label} não respondeu agora (HTTP {status}). Tente de novo em instantes.")
                : new CredentialTestOutcome(CredentialTestVerdict.Refused,
                    $"O endpoint de token de {label} respondeu HTTP {status}. Confira a URL base de {label}.");
        }
        catch (HttpRequestException ex)
        {
            return NetworkFailure.Of(ex) == NetworkCause.HostNotFound
                ? new CredentialTestOutcome(CredentialTestVerdict.Refused, $"O endereço da URL base de {label} não existe. Confira a URL.")
                : new CredentialTestOutcome(CredentialTestVerdict.Unavailable,
                    $"O endpoint de token de {label} não respondeu agora (sem resposta). Tente de novo em instantes.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CredentialTestOutcome(CredentialTestVerdict.Unavailable,
                $"O endpoint de token de {label} não respondeu a tempo. Tente de novo em instantes.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static string EnvironmentLabel(string environment) => environment switch
    {
        "sandbox" => "Sandbox",
        "production" => "Produção",
        _ => environment,
    };

    // A credencial da seção e o segredo do cofre. Sem um ou outro, rejeição que aponta para a tela — e nenhum pedido.
    private async Task<AvalaraResolvedCredential> ResolveAsync(AvalaraOutboundSettings settings, CancellationToken ct)
    {
        AvalaraClientCredential credential = settings.Credential;
        Uri endpoint = settings.TokenEndpoint(_options.TokenPath);
        string? secret = await _secrets.GetAsync(credential.SecretName, ct);
        if (string.IsNullOrEmpty(secret))
        {
            throw settings.SecretNotConfigured(vaultLacksValue: true);
        }

        return new AvalaraResolvedCredential(settings.TenantId, settings.Environment, endpoint, credential.ClientId, secret);
    }

    private bool TryCached(CacheKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AvalaraAccessToken? token)
    {
        token = null;
        DateTimeOffset now = _clock.GetUtcNow();

        if (_refusals.TryGetValue(key, out Refusal? refusal))
        {
            if (refusal.Until > now)
            {
                throw new DispatchRejectedException(refusal.Reason);
            }

            _refusals.TryRemove(new KeyValuePair<CacheKey, Refusal>(key, refusal));
        }

        // A margem antecipa a renovação, evitando usar um token que vence no meio da requisição.
        if (_cache.TryGetValue(key, out CachedToken? entry) && entry.ExpiresAt - _options.TokenRenewalMargin > now)
        {
            token = new AvalaraAccessToken(key.TenantId, key.Environment, entry.Value, isFresh: false, key);
            return true;
        }

        return false;
    }

    private async Task<AvalaraAccessToken> FetchAsync(AvalaraResolvedCredential credential, CacheKey key, CancellationToken ct)
    {
        // client_credentials com corpo JSON (application/json), e não formulário, como na coleção do Postman do cliente.
        // Verificado contra o sandbox em 2026-09-27 (design D13), junto com a leitura da resposta (access_token, token_type
        // bearer, expires_in em segundos) e da recusa (HTTP 400 com {"error": "<texto livre>"}, sem error_description).
        using var request = new HttpRequestMessage(HttpMethod.Post, credential.TokenEndpoint)
        {
            Content = JsonContent.Create(new TokenRequestBody(credential.ClientId, credential.Secret)),
        };

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);   // a troca com o endpoint de token nunca é fotografada

        // Credencial recusada: retentar não conserta e pode bloquear a conta. Lembrada, e rejeição com o motivo.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            string reason = $"Configuração do conector: a plataforma recusou a credencial do tenant '{credential.TenantId}' no ambiente "
                + $"'{credential.Environment}' ({RefusalDetail((int)response.StatusCode, TokenExchangeRedaction.For(body, [credential.Secret]).Redact(body).Text)}). "
                // Os dois campos, sempre, e nunca o que o texto da plataforma cita: um client_secret errado volta como
                // "client_id invalid" (sandbox, 2026-09-27). Seguir o texto mandaria o administrador olhar o campo errado.
                + "Confira o Client ID e o Client Secret na tela de conectores.";
            _refusals[key] = new Refusal(reason, _clock.GetUtcNow() + _options.CredentialRefusalHold);
            throw new DispatchRejectedException(reason);
        }

        // 5xx, 429 e o resto: transitório, no retry nativo. A mensagem do EnsureSuccessStatusCode traz só o status.
        response.EnsureSuccessStatusCode();

        (string? value, int? expiresIn) = ReadToken(body);
        if (string.IsNullOrEmpty(value))
        {
            throw new DispatchRejectedException(
                $"Configuração do conector: o endpoint de token do tenant '{credential.TenantId}' no ambiente '{credential.Environment}' "
                + $"respondeu sem token (HTTP {(int)response.StatusCode}).");
        }

        // Sem validade, ou com uma que vence dentro da margem: usa agora e não guarda. Inventar validade seria chutar a
        // regra da plataforma.
        if (expiresIn is { } seconds && TimeSpan.FromSeconds(seconds) > _options.TokenRenewalMargin)
        {
            _cache[key] = new CachedToken(value, _clock.GetUtcNow().AddSeconds(seconds));
        }
        else if (_warnedWithoutValidity.TryAdd(key, 0))
        {
            _logger.LogWarning(
                "O endpoint de token do tenant {Tenant} no ambiente {Environment} respondeu sem expires_in utilizável; o token é usado sem cache, e cada envio pede outro.",
                credential.TenantId, credential.Environment);
        }

        return new AvalaraAccessToken(credential.TenantId, credential.Environment, value, isFresh: true, key);
    }

    // O código e a descrição do erro do OAuth ("invalid_client — …"), do corpo já redigido pela regra da troca de token:
    // a descrição pode ecoar o segredo, a sessão ou o login. O corpo em si nunca entra.
    private static string RefusalDetail(int status, string body)
    {
        string? error = null, description = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                error = Text(doc.RootElement, "error");
                description = Text(doc.RootElement, "error_description");
            }
        }
        catch (JsonException)
        {
            // Sem JSON, fica só o status.
        }

        return (error, description) switch
        {
            (null, null) => $"HTTP {status}, sem código de erro",
            (null, _) => $"HTTP {status}: {description}",
            (_, null) => $"HTTP {status}: {error}",
            _ => $"HTTP {status}: {error} — {description}",
        };
    }

    private static (string? Value, int? ExpiresIn) ReadToken(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            int? expiresIn = doc.RootElement.TryGetProperty("expires_in", out JsonElement e) && e.ValueKind == JsonValueKind.Number
                && e.TryGetInt32(out int seconds)
                    ? seconds
                    : null;
            return (Text(doc.RootElement, "access_token"), expiresIn);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // O corpo do pedido de token. Classe, e não record: nenhum ToString imprime o segredo.
    private sealed class TokenRequestBody(string clientId, string secret)
    {
        [JsonPropertyName("grant_type")]
        public string GrantType => "client_credentials";

        [JsonPropertyName("client_id")]
        public string ClientId { get; } = clientId;

        [JsonPropertyName("client_secret")]
        public string ClientSecret { get; } = secret;

        // ORIGEM: a coleção do Postman do cliente, e não documentação da plataforma. Mandamos porque é o que o cliente
        // manda; o efeito na plataforma não foi verificado. Não tratar como contrato até haver documentação ou evidência.
        [JsonPropertyName("disableTokenRefresh")]
        public bool DisableTokenRefresh => true;
    }

    // A chave nunca vai para log: a impressão do segredo é derivada dele.
    private sealed record CacheKey(string TenantId, string Environment, string TokenEndpoint, string ClientId, string SecretFingerprint)
    {
        public static CacheKey Of(AvalaraResolvedCredential credential) => new(
            credential.TenantId, credential.Environment, credential.TokenEndpoint.AbsoluteUri, credential.ClientId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential.Secret))));

        public override string ToString() => $"CacheKey {{ Tenant = {TenantId}, Environment = {Environment} }}";
    }

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt)
    {
        public override string ToString() => $"CachedToken {{ ExpiresAt = {ExpiresAt:O} }}";
    }

    private sealed record Refusal(string Reason, DateTimeOffset Until);
}
