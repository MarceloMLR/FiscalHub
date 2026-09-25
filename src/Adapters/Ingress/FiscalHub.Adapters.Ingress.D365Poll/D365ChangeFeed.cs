using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Feed de mudanças do D365 F&amp;O sobre a <c>FSFiscalDocumentBRs</c> (ADR-0024): janela por data em
/// <c>SysModifiedDateTime</c>, paginação por keyset em (<c>SysModifiedDateTime</c>, <c>FiscalDocumentRecId</c>)
/// com <c>$top</c> — nunca pelo <c>@odata.nextLink</c>, que no F&amp;O é offset e pula linha quando uma nota
/// já lida é atualizada. Cada cabeçalho vira uma referência leve; a montagem é de outra fatia.
/// </summary>
internal sealed class D365ChangeFeed : IDocumentChangeFeed
{
    public const string OriginName = "Dynamics365";

    private const string EntitySet = "data/FSFiscalDocumentBRs";
    private const string Select = "dataAreaId,Voucher,Model,Direction,Status,FiscalDocumentNumber,FiscalDocumentSeries,SysModifiedDateTime,FiscalDocumentRecId";

    private readonly HttpClient _http;
    private readonly IConnectorProfileStore _profiles;
    private readonly ID365TokenProvider _tokens;
    private readonly D365ChangeFeedOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<D365ChangeFeed> _logger;

    public D365ChangeFeed(
        HttpClient http,
        IConnectorProfileStore profiles,
        ID365TokenProvider tokens,
        D365ChangeFeedOptions options,
        TimeProvider clock,
        ILogger<D365ChangeFeed> logger)
    {
        _http = http;
        _profiles = profiles;
        _tokens = tokens;
        _options = options;
        _clock = clock;
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
            ODataPage body;
            using (HttpResponseMessage response = await SendAsync(url, connection, ct))
            {
                scanStartedAt ??= response.Headers.Date;
                body = await response.Content.ReadFromJsonAsync<ODataPage>(ct)
                    ?? throw new InvalidOperationException("Resposta vazia do OData do F&O.");
            }

            List<Row> rows = body.Value ?? [];
            var references = new List<DocumentReference>(rows.Count);
            DateTimeOffset? highest = null;

            foreach (Row row in rows)
            {
                DateTimeOffset modified = ParseModified(row);
                highest = highest is null || modified > highest ? modified : highest;

                // Registro ruim não trava a marca: aviso e segue (falha isolada por documento).
                if (Map(row, tenantId, settings) is { } reference)
                {
                    references.Add(reference);
                }
            }

            // Página curta = fim da leitura. Aí a marca acompanha o relógio do lado F&O (ADR-0024 §2),
            // senão um tenant parado reenfileiraria a cauda da janela para sempre.
            bool last = rows.Count < settings.PageSize;
            if (last && scanStartedAt is { } serverNow && (highest is null || serverNow > highest))
            {
                highest = serverNow;
            }

            yield return new ChangeFeedPage { References = references, HighWatermark = highest };

            if (last)
            {
                yield break;
            }

            Row tail = rows[^1];
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
            .Append("&$select=").Append(Uri.EscapeDataString(Select))
            .Append("&$orderby=").Append(Uri.EscapeDataString("SysModifiedDateTime,FiscalDocumentRecId"))
            .Append("&$top=").Append(settings.PageSize.ToString(CultureInfo.InvariantCulture))
            .Append("&$filter=").Append(Uri.EscapeDataString(filter));

        return new Uri($"{settings.Url.GetLeftPart(UriPartial.Authority)}/{EntitySet}?{query}");
    }

    /// <summary>
    /// Rotina única de envio: repete a MESMA requisição (mesma âncora) em 429, ou 503 com <c>Retry-After</c>,
    /// honrando o intervalo pedido. Espera acima do teto ou tentativas esgotadas → throttling, e o worker
    /// adia o tenant. Qualquer outro erro falha a leitura na hora.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Uri url, D365Connection connection, CancellationToken ct)
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

    private DocumentReference? Map(Row row, string tenantId, D365InboundSettings settings)
    {
        if (string.IsNullOrWhiteSpace(row.DataAreaId) || string.IsNullOrWhiteSpace(row.Voucher))
        {
            _logger.LogWarning(
                "Cabeçalho do D365 sem empresa ou voucher ficou fora da fila (tenant {Tenant}, empresa '{Company}', voucher '{Voucher}', modelo '{Model}', RecId {RecId}).",
                tenantId, row.DataAreaId, row.Voucher, row.Model, row.FiscalDocumentRecId);
            return null;
        }

        if (row.Model is null || !settings.ModelTypes.TryGetValue(row.Model, out DocumentType type))
        {
            _logger.LogWarning(
                "Modelo '{Model}' fora do mapa do tenant {Tenant}: empresa {Company}, voucher {Voucher} ficou fora da fila.",
                row.Model, tenantId, row.DataAreaId, row.Voucher);
            return null;
        }

        return new DocumentReference
        {
            TenantId = tenantId,
            Type = type,
            NaturalKey = $"{row.DataAreaId}|{row.Voucher}",
            Locator = $"d365/{Uri.EscapeDataString(row.DataAreaId)}/{Uri.EscapeDataString(row.Voucher)}",
            Trigger = IngestionTrigger.Event,
        };
    }

    private static DateTimeOffset ParseModified(Row row)
        => DateTimeOffset.TryParse(row.SysModifiedDateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value)
            ? value
            : throw new InvalidOperationException($"SysModifiedDateTime inválido no F&O: '{row.SysModifiedDateTime}' (RecId {row.FiscalDocumentRecId}).");

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body = await response.Content.ReadAsStringAsync(ct);
        return body.Length > 300 ? body[..300] : body;
    }

    /// <summary>Última linha lida: o literal de data como veio e o RecId.</summary>
    private sealed record Anchor(string Modified, long RecId);

    // Contrato OData da FSFiscalDocumentBRs — preso ao adapter.
    private sealed record ODataPage
    {
        [JsonPropertyName("value")]
        public List<Row>? Value { get; init; }
    }

    private sealed record Row
    {
        [JsonPropertyName("dataAreaId")]
        public string? DataAreaId { get; init; }

        [JsonPropertyName("Voucher")]
        public string? Voucher { get; init; }

        [JsonPropertyName("Model")]
        public string? Model { get; init; }

        /// <summary>Guardado como texto: é o literal que volta na âncora do keyset.</summary>
        [JsonPropertyName("SysModifiedDateTime")]
        public string? SysModifiedDateTime { get; init; }

        [JsonPropertyName("FiscalDocumentRecId")]
        public long FiscalDocumentRecId { get; init; }
    }
}
