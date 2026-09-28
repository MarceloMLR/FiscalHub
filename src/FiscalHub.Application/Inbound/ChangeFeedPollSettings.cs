using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Seção <c>poll</c> das settings do adapter de entrada (ADR-0019): o contrato do worker de feed de
/// mudanças, igual para qualquer origem. O resto das settings (url, autenticação, página…) é do adapter.
/// Sem a seção, o poll fica desligado.
/// </summary>
public sealed record ChangeFeedPollSettings
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan DefaultOverlap = TimeSpan.FromSeconds(300);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // A forma que o Parse aceita, por campo, para a mensagem de quem grava. O nome é lido sem caixa, como no Parse.
    private static readonly Dictionary<string, string> AcceptedForms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enabled"] = "verdadeiro ou falso",
        ["intervalSeconds"] = "um número inteiro de segundos, maior que zero",
        ["overlapSeconds"] = "um número inteiro de segundos, de pelo menos 1",
        ["startFrom"] = "uma data e hora ISO 8601, como 2015-01-01T00:00:00Z",
    };

    /// <summary>Poll ligado para o tenant. Ausente = desligado.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// A seção <c>poll</c> existe nas settings. Sem ela, o poll também fica desligado, mas por ausência de configuração,
    /// e não por decisão: o poller avisa, porque um coletor desligado em silêncio é o pior modo de falha.
    /// </summary>
    public bool Configured { get; init; }

    /// <summary>Intervalo mínimo entre dois polls do tenant, contado do fim do anterior.</summary>
    public TimeSpan Interval { get; init; } = DefaultInterval;

    /// <summary>
    /// Quanto a consulta volta antes da marca d'água. Absorve diferença de relógio e gravação visível com
    /// atraso — e relê linhas com o mesmo timestamp da marca que ficaram para uma página não lida. Por isso
    /// nunca é zero.
    /// </summary>
    public TimeSpan Overlap { get; init; } = DefaultOverlap;

    /// <summary>Marca inicial, usada só quando o cursor ainda não tem marca (ex.: backfill a partir de 2015).</summary>
    public DateTimeOffset? StartFrom { get; init; }

    /// <summary>
    /// Como <see cref="Parse"/>, sem lançar: settings ilegíveis dão <c>false</c>. É para quem só mostra o estado (o
    /// <c>/info</c>); o poller usa o <see cref="Parse"/>, e a exceção vira a falha registrada do tenant.
    /// </summary>
    public static bool TryParse(string? inboundSettingsJson, [NotNullWhen(true)] out ChangeFeedPollSettings? settings)
    {
        try
        {
            settings = Parse(inboundSettingsJson);
            return true;
        }
        catch (ConnectorSettingsException)
        {
            settings = null;
            return false;
        }
    }

    /// <summary>Lê a seção <c>poll</c> do JSON de settings do adapter. Inválido → <see cref="ConnectorSettingsException"/>.</summary>
    public static ChangeFeedPollSettings Parse(string? inboundSettingsJson)
    {
        SettingsDto? root;
        try
        {
            root = JsonSerializer.Deserialize<SettingsDto>(
                string.IsNullOrWhiteSpace(inboundSettingsJson) ? "{}" : inboundSettingsJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new ConnectorSettingsException("Settings do adapter de entrada não são um JSON válido.", ex);
        }

        PollDto? poll = root?.Poll;
        if (poll is null)
        {
            return new ChangeFeedPollSettings();
        }

        int intervalSeconds = poll.IntervalSeconds ?? (int)DefaultInterval.TotalSeconds;
        if (intervalSeconds <= 0)
        {
            throw new ConnectorSettingsException($"poll.intervalSeconds deve ser maior que zero (veio {intervalSeconds}).");
        }

        int overlapSeconds = poll.OverlapSeconds ?? (int)DefaultOverlap.TotalSeconds;
        if (overlapSeconds < 1)
        {
            throw new ConnectorSettingsException($"poll.overlapSeconds deve ser de pelo menos 1 (veio {overlapSeconds}).");
        }

        return new ChangeFeedPollSettings
        {
            Configured = true,
            Enabled = poll.Enabled ?? false,
            Interval = TimeSpan.FromSeconds(intervalSeconds),
            Overlap = TimeSpan.FromSeconds(overlapSeconds),
            StartFrom = poll.StartFrom?.ToUniversalTime(),
        };
    }

    /// <summary>
    /// Os problemas da seção <c>poll</c> que uma gravação está escrevendo (design D8). Só é julgado o campo que ela
    /// introduz ou muda em relação a <paramref name="stored"/>, pelo mesmo <see cref="Parse"/> do poller. O valor
    /// inválido que já estava gravado e volta igual passa: a tela devolve a seção inteira, e não pode ficar trancada por
    /// um campo que ela não edita. Cada problema nomeia o campo (a partir de <c>poll</c>), o que veio e a forma aceita.
    /// </summary>
    /// <param name="written">A raiz das settings que vão ser gravadas.</param>
    /// <param name="stored">A raiz das settings gravadas do mesmo adapter; <c>null</c> quando tudo é novo.</param>
    public static IReadOnlyList<string> ProblemsInWrite(JsonObject written, JsonObject? stored)
    {
        var problems = new List<string>();
        (string? name, JsonNode? section) = FindSection(written);
        if (name is null || section is null)
        {
            return problems;   // sem seção (ou nula): o poll fica desligado, e isso é válido
        }

        JsonNode? storedSection = stored is null ? null : FindSection(stored).Section;
        if (section is not JsonObject fields)
        {
            if (!JsonNode.DeepEquals(section, storedSection))
            {
                problems.Add($"{name} precisa ser um objeto (veio {Describe(section)}).");
            }

            return problems;
        }

        foreach ((string field, JsonNode? value) in fields)
        {
            if (storedSection is JsonObject storedFields
                && storedFields.TryGetPropertyValue(field, out JsonNode? storedValue)
                && JsonNode.DeepEquals(value, storedValue))
            {
                continue;   // voltou igual: não é esta gravação que o escreve
            }

            var probe = new JsonObject { [name] = new JsonObject { [field] = value?.DeepClone() } };
            try
            {
                Parse(probe.ToJsonString());
            }
            catch (ConnectorSettingsException)
            {
                string form = AcceptedForms.TryGetValue(field, out string? accepted) ? accepted : "um valor que o coletor leia";
                problems.Add($"{name}.{field} precisa ser {form} (veio {Describe(value)}).");
            }
        }

        return problems;
    }

    // A seção é achada sem caixa, como o Parse a lê (opções JSON da Web).
    private static (string? Name, JsonNode? Section) FindSection(JsonObject root)
    {
        foreach ((string key, JsonNode? value) in root)
        {
            if (string.Equals(key, "poll", StringComparison.OrdinalIgnoreCase))
            {
                return (key, value);
            }
        }

        return (null, null);
    }

    private static string Describe(JsonNode? value) => value?.GetValueKind() switch
    {
        null or JsonValueKind.Null => "nulo",
        JsonValueKind.String => "texto",
        JsonValueKind.Number => value.ToJsonString(),
        JsonValueKind.True => "verdadeiro",
        JsonValueKind.False => "falso",
        JsonValueKind.Array => "uma lista",
        _ => "um objeto",
    };

    private sealed record SettingsDto
    {
        public PollDto? Poll { get; init; }
    }

    private sealed record PollDto
    {
        public bool? Enabled { get; init; }

        public int? IntervalSeconds { get; init; }

        public int? OverlapSeconds { get; init; }

        public DateTimeOffset? StartFrom { get; init; }
    }
}
