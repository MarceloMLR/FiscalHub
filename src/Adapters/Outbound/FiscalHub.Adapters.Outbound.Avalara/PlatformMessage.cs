using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// Extrai o motivo humano de uma recusa da plataforma (design D10). Com o mapa de erros por campo do
/// <c>ProblemDetails</c>, a forma que o sandbox mostrou, o motivo é um resumo curto: quantos campos e os três primeiros.
/// A lista inteira fica na foto da resposta. Nos outros formatos, a extração é tolerante: junta os textos das
/// propriedades de mensagem conhecidas (lista de mensagens, <c>ProblemDetails</c> sem mapa, objetos com descrição); sem
/// nenhuma, devolve o JSON compactado; sem JSON, o texto. O status nativo nunca entra — só o texto da plataforma atravessa o adapter (ADR-0003). Recebe o corpo já
/// redigido pelo <see cref="SensitiveText"/> (ADR-0027): o motivo nunca vê o token em uso.
/// </summary>
internal static partial class PlatformMessage
{
    public const int MaxLength = 1000;

    // Para leitura humana no dashboard: sem escapar acentos (o React escapa o texto na tela).
    private static readonly JsonSerializerOptions CompactOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly HashSet<string> MessageProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "mensagem", "mensagens", "message", "messages", "erro", "erros", "error", "errors", "detail", "title", "descricao",
    };

    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "status", "type", "traceId",
    };

    /// <summary>
    /// Motivo de uma recusa no envio (HTTP 400/422). Nunca vazio: sem mensagem reconhecida, vale o JSON compactado;
    /// sem JSON, o texto; sem corpo, o status HTTP.
    /// </summary>
    public static string Extract(string? body, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"HTTP {statusCode} sem corpo";
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return FindMessages(doc.RootElement) ?? Truncate(JsonSerializer.Serialize(doc.RootElement, CompactOpts));
        }
        catch (JsonException)
        {
            return Truncate(Whitespace().Replace(body, " ").Trim());
        }
    }

    /// <summary>
    /// Só as mensagens reconhecidas numa resposta JSON, ou <c>null</c> se não houver. É o que a consulta de status usa:
    /// ali a resposta traz o status nativo, e o fallback para o corpo cru o vazaria.
    /// </summary>
    public static string? FindMessages(JsonElement root)
    {
        // Mapa de erros por campo (ValidationProblemDetails): o motivo é o resumo, e a lista inteira fica na foto, de onde a
        // tela a lê. O title, genérico, não entra (establishment-and-readable-dashboard, D7).
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Object
            && errors.EnumerateObject().Any())
        {
            return Truncate(Summary([.. errors.EnumerateObject().Select(p => p.Name)]));
        }

        var found = new List<string>();
        if (root.ValueKind == JsonValueKind.String)
        {
            found.Add(root.GetString()!);
        }
        else
        {
            Scan(root, found);
        }

        return found.Count > 0 ? Truncate(string.Join("; ", found)) : null;
    }

    private const int NamedFields = 3;

    // "6 campos com erro: operacao, tipoPagamento, parceiro.Codigo e mais 3" — os caminhos na ordem da resposta.
    private static string Summary(IReadOnlyList<string> fields)
    {
        string count = fields.Count == 1 ? "1 campo com erro" : $"{fields.Count} campos com erro";
        string named = string.Join(", ", fields.Take(NamedFields));
        return fields.Count > NamedFields ? $"{count}: {named} e mais {fields.Count - NamedFields}" : $"{count}: {named}";
    }

    private static string Truncate(string text)
        => text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 1), "…");

    // Fora de uma propriedade de mensagem: procura propriedades de mensagem em qualquer nível.
    private static void Scan(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (IgnoredProperties.Contains(property.Name))
                    {
                        continue;
                    }

                    if (MessageProperties.Contains(property.Name))
                    {
                        Collect(property.Value, found, field: null);
                    }
                    else
                    {
                        Scan(property.Value, found);
                    }
                }

                break;

            case JsonValueKind.Array:   // lista na raiz: cada item é uma mensagem
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, found, field: null);
                }

                break;

            // Texto fora de propriedade de mensagem ("campo": "cfop") não é motivo.
        }
    }

    // Dentro de uma propriedade de mensagem: todo texto conta. Um objeto com propriedades de mensagem contribui só
    // com elas; sem nenhuma, é um mapa de erros por campo (ProblemDetails.errors), e cada texto vira "campo: texto".
    private static void Collect(JsonElement value, List<string> found, string? field)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                string text = value.GetString()!;
                found.Add(field is null ? text : $"{field}: {text}");
                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Collect(item, found, field);
                }

                break;

            case JsonValueKind.Object:
                List<JsonProperty> properties = [.. value.EnumerateObject().Where(p => !IgnoredProperties.Contains(p.Name))];
                List<JsonProperty> messages = [.. properties.Where(p => MessageProperties.Contains(p.Name))];
                if (messages.Count > 0)
                {
                    messages.ForEach(p => Collect(p.Value, found, field));
                }
                else
                {
                    properties.ForEach(p => Collect(p.Value, found, p.Name));
                }

                break;
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
