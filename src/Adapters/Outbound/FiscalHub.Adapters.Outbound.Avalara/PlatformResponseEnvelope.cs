using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// O envelope da resposta da plataforma (ADR-0027, design D8): o que o dispatcher fotografa no trace e o que a sonda do
/// sandbox grava como evidência — a mesma forma, e a mesma redação.
/// <list type="bullet">
///   <item><b>request:</b> só o método e a URL sem query string. Nenhum cabeçalho de requisição.</item>
///   <item><b>headers:</b> a lista fechada de <see cref="Headers"/>. <c>Set-Cookie</c> e <c>WWW-Authenticate</c> ficam fora.</item>
///   <item><b>body:</b> o corpo já redigido; JSON quando é JSON, texto nos outros casos.</item>
///   <item><b>redactions:</b> quanto foi redigido. Acima de zero, a plataforma ecoou algo sensível.</item>
/// </list>
/// </summary>
internal static class PlatformResponseEnvelope
{
    /// <summary>Os cabeçalhos de resposta que entram. A lista cresce com evidência, se o sandbox usar outro de correlação.</summary>
    public static readonly string[] Headers = ["Content-Type", "Date", "X-Correlation-Id", "X-Request-Id", "Request-Id", "traceparent"];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="redactedBody">O corpo, já passado pelo <see cref="SensitiveText"/>.</param>
    /// <param name="redactions">Quanto a redação do corpo contou.</param>
    /// <param name="knownValues">Os valores que também saem dos cabeçalhos (o token em uso).</param>
    public static string Build(
        string exchange, HttpRequestMessage request, HttpResponseMessage response, string redactedBody, int redactions,
        IEnumerable<string?> knownValues, DateTimeOffset receivedAt)
    {
        string?[] known = [.. knownValues];
        var headers = new JsonObject();
        foreach (string name in Headers)
        {
            if (response.Headers.TryGetValues(name, out IEnumerable<string>? values)
                || response.Content.Headers.TryGetValues(name, out values))
            {
                (string value, int count) = SensitiveText.Redact(string.Join(", ", values), known);
                headers[name] = value;
                redactions += count;
            }
        }

        var envelope = new JsonObject
        {
            ["exchange"] = exchange,
            ["request"] = new JsonObject
            {
                ["method"] = request.Method.Method,
                ["url"] = request.RequestUri!.GetLeftPart(UriPartial.Path),   // sem query string
            },
            ["response"] = new JsonObject
            {
                ["status"] = (int)response.StatusCode,
                ["receivedAt"] = receivedAt.ToString("O"),
                ["headers"] = headers,
                ["body"] = BodyNode(redactedBody),
            },
            ["redactions"] = redactions,
        };

        return envelope.ToJsonString(Options);
    }

    private static JsonNode? BodyNode(string body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create(body);
        }
    }
}
