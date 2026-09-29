using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// O envelope da resposta da plataforma (ADR-0027, design D8): o que o dispatcher fotografa no trace e o que a sonda do
/// sandbox grava como evidência — a mesma forma, e a mesma redação.
/// <list type="bullet">
///   <item><b>request:</b> só o método e a URL sem query string — a URL prova para qual ambiente a nota foi. Nenhum
///   cabeçalho de requisição. No envio com omissões, também o que o hub declarou que o contrato não levou
///   (<c>omissions</c>): é o único lugar em que a omissão de uma nota recusada fica gravada.</item>
///   <item><b>headers:</b> a lista fechada de <see cref="Headers"/>. <c>Set-Cookie</c> e <c>WWW-Authenticate</c> ficam fora.</item>
///   <item><b>body:</b> o corpo já redigido; JSON quando é JSON, texto nos outros casos. O dispatcher o passa sem o ruído
///   do <c>ProblemDetails</c> (<see cref="ProblemDetailsNoise"/>); a sonda, cru.</item>
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

    /// <param name="redactedBody">O corpo que vai para a foto, já redigido pela mesma regra de <paramref name="redactHeader"/>.</param>
    /// <param name="redactions">Quanto a redação do corpo contou.</param>
    /// <param name="redactHeader">A regra aplicada a cada valor de cabeçalho: a do envio (o token em uso) ou a da troca de
    /// token (<see cref="TokenExchangeRedaction"/>).</param>
    /// <param name="omissions">O que o hub declarou que o contrato não levou nesta requisição. Vazio ou nulo, o campo não
    /// aparece.</param>
    public static string Build(
        string exchange, HttpRequestMessage request, HttpResponseMessage response, string redactedBody, int redactions,
        Func<string, (string Text, int Redactions)> redactHeader, DateTimeOffset receivedAt, IReadOnlyList<string>? omissions = null)
    {
        var headers = new JsonObject();
        foreach (string name in Headers)
        {
            if (response.Headers.TryGetValues(name, out IEnumerable<string>? values)
                || response.Content.Headers.TryGetValues(name, out values))
            {
                (string value, int count) = redactHeader(string.Join(", ", values));
                headers[name] = value;
                redactions += count;
            }
        }

        var requestNode = new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = request.RequestUri!.GetLeftPart(UriPartial.Path),   // sem query string
        };
        if (omissions is { Count: > 0 })
        {
            requestNode["omissions"] = new JsonArray([.. omissions.Select(o => JsonValue.Create(o))]);
        }

        var envelope = new JsonObject
        {
            ["exchange"] = exchange,
            ["request"] = requestNode,
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
