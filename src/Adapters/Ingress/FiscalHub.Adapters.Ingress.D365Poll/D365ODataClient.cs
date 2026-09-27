using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Envio ao OData do F&amp;O compartilhado pelo feed e pela montagem: bearer do tenant, <c>Accept: application/json</c>
/// e throttling — repete a MESMA requisição em 429, ou 503 com <c>Retry-After</c>, honrando o intervalo pedido.
/// Espera acima do teto ou tentativas esgotadas → <see cref="ChangeFeedThrottledException"/>; qualquer outro erro
/// falha na hora.
/// </summary>
internal sealed class D365ODataClient
{
    private readonly HttpClient _http;
    private readonly ID365TokenProvider _tokens;
    private readonly D365ChangeFeedOptions _options;
    private readonly TimeProvider _clock;

    public D365ODataClient(HttpClient http, ID365TokenProvider tokens, D365ChangeFeedOptions options, TimeProvider clock)
    {
        _http = http;
        _tokens = tokens;
        _options = options;
        _clock = clock;
    }

    /// <summary>GET com retry de throttling. Devolve a resposta de sucesso; quem chama a descarta.</summary>
    public async Task<HttpResponseMessage> SendAsync(Uri url, D365Connection connection, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            string token = await _tokens.GetTokenAsync(connection, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            bool throttled = response.StatusCode == HttpStatusCode.TooManyRequests
                || (response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null);
            if (!throttled)
            {
                string detail = await ReadSnippetAsync(response, ct);
                HttpStatusCode status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"OData do F&O respondeu {(int)status} {status}: {detail}", null, status);
            }

            TimeSpan wait = RetryAfter(response.Headers.RetryAfter) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            response.Dispose();

            if (wait > _options.MaxThrottleWait)
            {
                throw new ChangeFeedThrottledException(wait, $"F&O pediu {wait.TotalSeconds:0}s de espera (throttling); o tenant fica adiado.");
            }

            if (attempt >= _options.MaxAttempts)
            {
                throw new ChangeFeedThrottledException(wait, $"F&O seguiu em throttling depois de {attempt} tentativas.");
            }

            await Task.Delay(wait, _clock, ct);
        }
    }

    /// <summary>
    /// Lê todas as linhas de uma coleção, seguindo o <c>@odata.nextLink</c> até o fim. Vale para o conjunto de um
    /// único documento lançado (linhas, impostos, encargos), que não é janela móvel — diferente do feed, que
    /// pagina por keyset justamente porque offset pula linha em tabela viva (ADR-0024).
    /// </summary>
    public async Task<IReadOnlyList<JsonElement>> GetAllAsync(Uri url, D365Connection connection, CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        Uri? next = url;
        while (next is not null)
        {
            using HttpResponseMessage response = await SendAsync(next, connection, ct);
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument page = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (page.RootElement.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array)
            {
                rows.AddRange(value.EnumerateArray().Select(row => row.Clone()));
            }

            next = page.RootElement.TryGetProperty("@odata.nextLink", out JsonElement link) && link.GetString() is { } href
                ? new Uri(href)
                : null;
        }

        return rows;
    }

    private TimeSpan? RetryAfter(RetryConditionHeaderValue? header)
    {
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            TimeSpan until = date - _clock.GetUtcNow();
            return until > TimeSpan.Zero ? until : TimeSpan.Zero;
        }

        return null;
    }

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body = await response.Content.ReadAsStringAsync(ct);
        return body.Length > 300 ? body[..300] : body;
    }
}
