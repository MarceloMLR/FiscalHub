using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// O corpo que vai para a foto da resposta, sem o ruído do <c>ProblemDetails</c> (design D10 da change
/// establishment-and-readable-dashboard). Com o mapa de erros por campo (<c>errors</c> objeto), saem do topo o
/// <c>type</c> (o link fixo da RFC), o <c>title</c> (o texto genérico da validação) e o <c>status</c> quando repete o HTTP.
/// Todo o resto fica, e em particular o <c>errors</c> e o <c>traceId</c>, que é como se abre chamado na plataforma. Sem o
/// mapa, ou sem JSON, o corpo fica como veio: a resposta de status nunca perde o status nativo, e num
/// <c>ProblemDetails</c> sem mapa o <c>title</c> pode ser a única mensagem. Vale para a foto do trace; a sonda do sandbox
/// grava o corpo cru, porque existe para registrar a forma da plataforma.
/// </summary>
internal static class ProblemDetailsNoise
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="body">O corpo já redigido (<see cref="SensitiveText"/>).</param>
    /// <param name="httpStatus">O status HTTP da resposta, para reconhecer o <c>status</c> repetido.</param>
    public static string Strip(string body, int httpStatus)
    {
        JsonNode? root;
        try
        {
            root = body.Length == 0 ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }

        if (root is not JsonObject obj || obj["errors"] is not JsonObject)
        {
            return body;
        }

        obj.Remove("type");
        obj.Remove("title");
        if (obj["status"] is JsonValue status && status.TryGetValue(out int value) && value == httpStatus)
        {
            obj.Remove("status");
        }

        return obj.ToJsonString(Compact);
    }
}
