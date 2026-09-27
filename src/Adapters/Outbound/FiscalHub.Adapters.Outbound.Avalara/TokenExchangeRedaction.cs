using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// A redação da troca com o endpoint de token (ADR-0027), onde ela é gravada ou ecoada: o motivo da recusa da credencial,
/// que vai ao dashboard, e a evidência da sonda (<c>out/token.json</c>), que existe para ser colada em PR. O trace do hub
/// nunca fotografa essa troca.
/// <list type="bullet">
///   <item><b>Credenciais</b> — o segredo em uso e o que a resposta traz com nome de credencial (<c>access_token</c>,
///   <c>refresh_token</c>…) — saem como <c>[redigido]</c>, por nome e por valor, pelo <see cref="SensitiveText"/>.</item>
///   <item><b>Identificadores da sessão e da conta</b> — <c>sessionId</c>, <c>userId</c>, <c>subId</c>, <c>appId</c> e o
///   <c>login</c>, que traz o nome da empresa — saem como <c>[mascarado]</c>, por nome e por valor.</item>
/// </list>
/// Os valores são tirados da própria resposta, e a mesma regra vale para o corpo e para os cabeçalhos. O resto
/// (<c>token_type</c>, <c>expires_in</c>) fica: é o que a evidência precisa mostrar.
/// </summary>
internal sealed class TokenExchangeRedaction
{
    public const string Mask = "[mascarado]";

    // Um valor curto só é mascarado pelo nome: por valor, um userId "4" apagaria o 4 do expires_in 86400.
    private const int MinValueLength = 6;

    // Comparados sem maiúscula, "_" ou "-", como os nomes do SensitiveText.
    private static readonly HashSet<string> IdentifierNames = ["sessionid", "userid", "subid", "appid", "login"];

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string?[] _credentials;
    private readonly string[] _identifiers;

    private TokenExchangeRedaction(string?[] credentials, string[] identifiers)
    {
        _credentials = credentials;
        _identifiers = identifiers;
    }

    /// <summary>A regra para uma troca: os valores conhecidos (o segredo em uso) e os que a resposta traz.</summary>
    public static TokenExchangeRedaction For(string? responseBody, IEnumerable<string?> knownValues)
    {
        var credentials = new List<string?>(knownValues);
        var identifiers = new List<string>();
        if (TryParse(responseBody, out JsonNode? root))
        {
            Collect(root, credentials, identifiers);
        }

        return new TokenExchangeRedaction([.. credentials], [.. identifiers.Where(v => v.Length >= MinValueLength).Distinct().OrderByDescending(v => v.Length)]);
    }

    public (string Text, int Redactions) Redact(string? text)
    {
        (string redacted, int count) = SensitiveText.Redact(text, _credentials);
        if (redacted.Length == 0)
        {
            return (redacted, count);
        }

        if (!TryParse(redacted, out JsonNode? root))
        {
            foreach (string value in _identifiers)
            {
                int occurrences = SensitiveText.Occurrences(redacted, value);
                if (occurrences > 0)
                {
                    redacted = redacted.Replace(value, Mask, StringComparison.Ordinal);
                    count += occurrences;
                }
            }

            return (redacted, count);
        }

        // No JSON, pelo nó: trocar um número no texto cru deixaria o JSON inválido.
        int masked = MaskNode(root);
        return masked == 0 ? (redacted, count) : (root!.ToJsonString(Compact), count + masked);
    }

    private int MaskNode(JsonNode? node)
    {
        int count = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach ((string name, JsonNode? value) in obj.ToList())
                {
                    if (IdentifierNames.Contains(SensitiveText.Normalize(name)))
                    {
                        if (value is not null && !IsMask(value))
                        {
                            obj[name] = Mask;
                            count++;
                        }
                    }
                    else if (Replacement(value, out JsonNode? replacement, out int replaced))
                    {
                        obj[name] = replacement;
                        count += replaced;
                    }
                    else
                    {
                        count += MaskNode(value);
                    }
                }

                break;
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (Replacement(array[i], out JsonNode? replacement, out int replaced))
                    {
                        array[i] = replacement;
                        count += replaced;
                    }
                    else
                    {
                        count += MaskNode(array[i]);
                    }
                }

                break;
        }

        return count;
    }

    // Um identificador ecoado num valor: dentro de um texto, a ocorrência; um número igual, o valor inteiro.
    private bool Replacement(JsonNode? node, out JsonNode? replacement, out int count)
    {
        replacement = null;
        count = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue(out string? text))
        {
            foreach (string id in _identifiers)
            {
                int occurrences = SensitiveText.Occurrences(text, id);
                if (occurrences > 0)
                {
                    text = text.Replace(id, Mask, StringComparison.Ordinal);
                    count += occurrences;
                }
            }

            replacement = count > 0 ? JsonValue.Create(text) : null;
            return count > 0;
        }

        if (value.GetValueKind() == JsonValueKind.Number && _identifiers.Contains(value.ToJsonString()))
        {
            replacement = JsonValue.Create(Mask);
            count = 1;
            return true;
        }

        return false;
    }

    private static void Collect(JsonNode? node, List<string?> credentials, List<string> identifiers)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string name, JsonNode? value) in obj)
                {
                    string normalized = SensitiveText.Normalize(name);
                    if (SensitiveText.IsSensitiveName(normalized) && ScalarText(value) is { } credential)
                    {
                        credentials.Add(credential);
                    }
                    else if (IdentifierNames.Contains(normalized) && ScalarText(value) is { } identifier)
                    {
                        identifiers.Add(identifier);
                    }
                    else
                    {
                        Collect(value, credentials, identifiers);
                    }
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Collect(item, credentials, identifiers);
                }

                break;
        }
    }

    private static string? ScalarText(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.ToJsonString(),
        _ => null,
    };

    private static bool IsMask(JsonNode value) => value is JsonValue v && v.TryGetValue(out string? s) && s == Mask;

    private static bool TryParse(string? text, out JsonNode? root)
    {
        root = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            root = JsonNode.Parse(text);
            return root is JsonObject or JsonArray;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
