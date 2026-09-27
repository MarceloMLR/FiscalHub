using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// A redação do que a plataforma devolve (ADR-0027): um ponto só, aplicado ao corpo cru antes da foto, do motivo e de
/// qualquer log. Três passadas, com a contagem do que foi redigido — acima de zero é sinal de que a plataforma ecoou
/// algo sensível:
/// <list type="number">
///   <item><b>por valor:</b> toda ocorrência dos valores conhecidos (o token em uso; no endpoint de token, o segredo);</item>
///   <item><b>por padrão:</b> <c>Bearer &lt;valor&gt;</c>, em qualquer texto;</item>
///   <item><b>por nome, quando é JSON:</b> o valor das propriedades de nome sensível, em qualquer nível.</item>
/// </list>
/// O custo aceito: um campo legítimo chamado <c>token</c> também é escondido, e o marcador fica visível.
/// </summary>
internal static partial class SensitiveText
{
    public const string Marker = "[redigido]";

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Comparados sem maiúscula, "_" ou "-".
    private static readonly HashSet<string> SensitiveNames =
        ["authorization", "accesstoken", "token", "refreshtoken", "idtoken", "clientsecret", "secret", "password", "senha", "apikey"];

    public static (string Text, int Redactions) Redact(string? text, IEnumerable<string?> knownValues)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text ?? string.Empty, 0);
        }

        int count = 0;

        // 1. Por valor. Os mais longos primeiro: um valor contido em outro não deixa sobra.
        foreach (string value in knownValues.Where(v => !string.IsNullOrEmpty(v)).Distinct().OrderByDescending(v => v!.Length)!)
        {
            int occurrences = Occurrences(text, value);
            if (occurrences > 0)
            {
                text = text.Replace(value, Marker, StringComparison.Ordinal);
                count += occurrences;
            }
        }

        // 2. Por padrão: o que vem depois de "Bearer", se ainda não é o marcador.
        text = BearerValue().Replace(text, match =>
        {
            count++;
            return match.Groups["scheme"].Value + Marker;
        });

        // 3. Por nome, quando o texto é JSON. Só re-serializa se mudou algo: o texto limpo fica como veio.
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return (text, count);
        }

        int byName = RedactNames(root);
        return byName == 0 ? (text, count) : (root!.ToJsonString(Compact), count + byName);
    }

    private static int RedactNames(JsonNode? node)
    {
        int count = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach ((string name, JsonNode? value) in obj.ToList())
                {
                    if (SensitiveNames.Contains(Normalize(name)))
                    {
                        if (value is not null && !(value is JsonValue v && v.TryGetValue(out string? s) && s == Marker))
                        {
                            obj[name] = Marker;
                            count++;
                        }
                    }
                    else
                    {
                        count += RedactNames(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    count += RedactNames(item);
                }

                break;
        }

        return count;
    }

    /// <summary>Se o nome (já normalizado) designa uma credencial ou um token.</summary>
    internal static bool IsSensitiveName(string normalizedName) => SensitiveNames.Contains(normalizedName);

    internal static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    internal static string Normalize(string name) => new([.. name.ToLowerInvariant().Where(c => c is not ('_' or '-'))]);

    // "Bearer" + espaço + um valor que não é o marcador; o valor para em espaço, aspas, vírgula ou ponto e vírgula.
    [GeneratedRegex(@"(?<scheme>\bbearer\s+)(?!\[redigido\])[^\s""',;]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerValue();
}
