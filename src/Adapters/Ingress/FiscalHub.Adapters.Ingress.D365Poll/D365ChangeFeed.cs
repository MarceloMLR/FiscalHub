using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Feed de mudanças do D365 F&amp;O sobre a <c>FSFiscalDocumentBRs</c> (ADR-0024): janela por data em
/// <c>SysModifiedDateTime</c>, paginação por keyset em (<c>SysModifiedDateTime</c>, <c>FiscalDocumentRecId</c>)
/// com <c>$top</c> — nunca pelo <c>@odata.nextLink</c>, que no F&amp;O é offset e pula linha quando uma nota
/// já lida é atualizada. Cada cabeçalho vira uma referência leve (<c>d365/&lt;empresa&gt;/&lt;RecId&gt;</c>), que o
/// <see cref="D365GoodsInvoiceSource"/> monta; cada item leva o carimbo, e a página, o horizonte estável (ADR-0025). O
/// mapeamento do cabeçalho é o mesmo da descoberta por período (<see cref="D365HeaderReference"/>).
/// </summary>
internal sealed class D365ChangeFeed : IDocumentChangeFeed
{
    public const string OriginName = "Dynamics365";

    private readonly D365ODataClient _client;
    private readonly IConnectorProfileStore _profiles;
    private readonly D365ChangeFeedOptions _options;
    private readonly ILogger<D365ChangeFeed> _logger;

    public D365ChangeFeed(
        HttpClient http,
        IConnectorProfileStore profiles,
        ID365TokenProvider tokens,
        D365ChangeFeedOptions options,
        TimeProvider clock,
        ILogger<D365ChangeFeed> logger)
    {
        _client = new D365ODataClient(http, tokens, options, clock);
        _profiles = profiles;
        _options = options;
        _logger = logger;
    }

    public string Origin => OriginName;

    public async IAsyncEnumerable<ChangeFeedPage> PullAsync(
        string tenantId, DateTimeOffset since, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Settings primeiro: configuração inválida falha sem tocar a rede.
        TenantConnectorProfile profile = await _profiles.GetAsync(tenantId, ct)
            ?? throw new ConnectorSettingsException($"Tenant '{tenantId}' sem perfil de conector.");
        D365InboundSettings settings = D365InboundSettings.Parse(profile.InboundSettings);
        var connection = new D365Connection(tenantId, settings.Url, settings.Auth);

        DateTimeOffset? scanStartedAt = null;   // relógio do web server F&O no início da varredura (header Date)
        Anchor? anchor = null;

        while (true)
        {
            Uri url = BuildUrl(settings, since, anchor);
            D365HeaderPage body;
            using (HttpResponseMessage response = await _client.SendAsync(url, connection, ct))
            {
                scanStartedAt ??= response.Headers.Date;
                body = await response.Content.ReadFromJsonAsync<D365HeaderPage>(ct)
                    ?? throw new InvalidOperationException("Resposta vazia do OData do F&O.");
            }

            List<D365HeaderRow> rows = body.Value ?? [];
            var items = new List<ChangeFeedItem>(rows.Count);
            DateTimeOffset? highest = null;

            foreach (D365HeaderRow row in rows)
            {
                DateTimeOffset modified = ParseModified(row);
                highest = highest is null || modified > highest ? modified : highest;

                // Registro ruim não trava a marca: aviso e segue (falha isolada por documento).
                if (D365HeaderReference.Map(row, tenantId, settings, _logger) is { } reference)
                {
                    items.Add(new ChangeFeedItem(reference, modified));
                }
            }

            // Página curta = fim da leitura. Aí a marca acompanha o relógio do lado F&O (ADR-0024 §2),
            // senão um tenant parado reenfileiraria a cauda da janela para sempre.
            bool last = rows.Count < settings.PageSize;
            if (last && scanStartedAt is { } serverNow && (highest is null || serverNow > highest))
            {
                highest = serverNow;
            }

            // Horizonte estável (design D16): o SysModifiedDateTime tem resolução de segundo, então um carimbo
            // muito recente ainda pode ser dado a outra gravação. Recuar a margem do relógio do início da
            // varredura cobre a resolução e a diferença entre o web server e quem carimba. Sem Date, nada é
            // definitivo e o motor não suprime nada desta leitura.
            DateTimeOffset? stableThrough = scanStartedAt - _options.StampSettleMargin;

            yield return new ChangeFeedPage { Items = items, HighWatermark = highest, StableThrough = stableThrough };

            if (last)
            {
                yield break;
            }

            D365HeaderRow tail = rows[^1];
            anchor = new Anchor(tail.SysModifiedDateTime!, tail.FiscalDocumentRecId);
        }
    }

    private static Uri BuildUrl(D365InboundSettings settings, DateTimeOffset since, Anchor? anchor)
    {
        // Arredonda o "desde" para baixo, ao segundo: só amplia a janela. A âncora usa o literal cru da
        // resposta, para o "eq" bater exatamente com o que o F&O guardou.
        string window = anchor is null
            ? $"SysModifiedDateTime gt {since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}"
            : $"(SysModifiedDateTime gt {anchor.Modified}) or (SysModifiedDateTime eq {anchor.Modified} and FiscalDocumentRecId gt {anchor.RecId.ToString(CultureInfo.InvariantCulture)})";

        string filter = window;
        if (settings.Companies.Count > 0)
        {
            string companies = string.Join(" or ", settings.Companies.Select(c => $"dataAreaId eq '{c.Replace("'", "''", StringComparison.Ordinal)}'"));
            filter = anchor is null ? $"{window} and ({companies})" : $"({window}) and ({companies})";
        }

        var query = new StringBuilder()
            .Append("cross-company=true")
            .Append("&$select=").Append(Uri.EscapeDataString(D365HeaderReference.Select))
            .Append("&$orderby=").Append(Uri.EscapeDataString("SysModifiedDateTime,FiscalDocumentRecId"))
            .Append("&$top=").Append(settings.PageSize.ToString(CultureInfo.InvariantCulture))
            .Append("&$filter=").Append(Uri.EscapeDataString(filter));

        return new Uri($"{settings.Url.GetLeftPart(UriPartial.Authority)}/{D365HeaderReference.EntitySet}?{query}");
    }

    private static DateTimeOffset ParseModified(D365HeaderRow row)
        => DateTimeOffset.TryParse(row.SysModifiedDateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value)
            ? value
            : throw new InvalidOperationException($"SysModifiedDateTime inválido no F&O: '{row.SysModifiedDateTime}' (RecId {row.FiscalDocumentRecId}).");

    /// <summary>Última linha lida: o literal de data como veio e o RecId.</summary>
    private sealed record Anchor(string Modified, long RecId);
}
