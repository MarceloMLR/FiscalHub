using System.Text.Json;
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

    /// <summary>Poll ligado para o tenant. Ausente = desligado.</summary>
    public bool Enabled { get; init; }

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
            Enabled = poll.Enabled ?? false,
            Interval = TimeSpan.FromSeconds(intervalSeconds),
            Overlap = TimeSpan.FromSeconds(overlapSeconds),
            StartFrom = poll.StartFrom?.ToUniversalTime(),
        };
    }

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
