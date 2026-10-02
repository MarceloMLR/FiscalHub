using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// A descoberta por período do D365 (change erp-company-directory-and-card-filters, D4), para a integração manual e a
/// agendada: as notas cujo grupo cai no recorte pedido.
/// <list type="bullet">
///   <item><b>O período:</b> o dia fiscal (<c>FiscalDocumentDate</c>) entre o dia do início e o do fim, cada um no fuso que
///   ele traz, sem conversão — o mesmo dia do grupo e dos cards. O D-1 do agendamento diário é "a data fiscal de
///   ontem".</item>
///   <item><b>A empresa e a filial:</b> os estabelecimentos do cadastro (<see cref="D365FiscalEstablishments"/>) com o CNPJ
///   da empresa e, com filial, o código dela. O filtro no F&amp;O é pelo par (<c>dataAreaId</c>, código), que é exato; o
///   CNPJ fica numa guarda, porque o documento o guarda formatado como o F&amp;O formata.</item>
///   <item><b>A leitura:</b> keyset por <c>FiscalDocumentRecId</c>, e nunca o <c>@odata.nextLink</c>, que é offset e pula
///   linha quando uma nota do período muda durante a leitura (ADR-0024).</item>
///   <item><b>A referência:</b> a mesma que o coletor publica (<see cref="D365HeaderReference"/>), com a origem
///   <c>Dynamics365</c>. Todos os modelos entram; o roteamento decide o que é ignorado.</item>
/// </list>
/// </summary>
internal sealed class D365DocumentDiscovery : IDocumentDiscovery
{
    public static readonly D365Read Read = new("das notas fiscais", "FSFiscalDocumentBRs", "FSFiscalDocumentBRView");

    private readonly D365ODataClient _client;
    private readonly IConnectorProfileStore _profiles;
    private readonly D365FiscalEstablishments _establishments;
    private readonly ILogger<D365DocumentDiscovery> _logger;

    public D365DocumentDiscovery(
        HttpClient http,
        IConnectorProfileStore profiles,
        ID365TokenProvider tokens,
        D365ChangeFeedOptions options,
        TimeProvider clock,
        ILogger<D365DocumentDiscovery> logger)
    {
        _client = new D365ODataClient(http, tokens, options, clock);
        _profiles = profiles;
        _establishments = new D365FiscalEstablishments(_client, logger);
        _logger = logger;
    }

    public string Origin => D365ChangeFeed.OriginName;

    public async Task<IReadOnlyList<DocumentReference>> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken ct = default)
    {
        string tenantId = criteria.TenantId;
        D365InboundSettings settings = await SettingsAsync(tenantId, ct);

        // O dia de cada ponta no próprio fuso: a tela e o agendador mandam BRT, e o dia não é convertido.
        DateOnly first = DateOnly.FromDateTime(criteria.Start.Date), last = DateOnly.FromDateTime(criteria.End.Date);
        if (last < first)
        {
            return [];
        }

        IReadOnlyList<D365Establishment>? scope = null;
        if (criteria.Company is not null || criteria.Establishment is not null)
        {
            scope = [.. (await _establishments.ReadAsync(tenantId, settings, ct))
                .Where(e => criteria.Company is null || string.Equals(e.Cnpj, criteria.Company, StringComparison.Ordinal))
                .Where(e => criteria.Establishment is null || string.Equals(e.Code, criteria.Establishment, StringComparison.Ordinal))];

            if (scope.Count == 0)
            {
                _logger.LogInformation(
                    "Descoberta por período do D365: a empresa '{Company}', filial '{Branch}', não está no cadastro do tenant {Tenant}. Nenhuma nota.",
                    criteria.Company, criteria.Establishment ?? "todas", tenantId);
                return [];
            }
        }

        var connection = new D365Connection(tenantId, settings.Url, settings.Auth);
        var found = new List<DocumentReference>();
        long? after = null;
        while (true)
        {
            Uri url = BuildUrl(settings, first, last, scope, criteria.DocumentNumber, after);
            List<D365HeaderRow> rows = await D365ReadFailures.GuardAsync(() => PageAsync(url, connection, ct), Read, tenantId, _logger, ct);

            foreach (D365HeaderRow row in rows)
            {
                if (D365HeaderReference.Map(row, tenantId, settings, _logger) is not { } reference)
                {
                    continue;
                }

                // A guarda pelo CNPJ: o documento com o CNPJ antigo de um estabelecimento que mudou de CNPJ fica fora, e o
                // resultado é exatamente "as notas cujo grupo é (empresa, filial)".
                if (criteria.Company is not null && !string.Equals(reference.Metadata!.CompanyCode, criteria.Company, StringComparison.Ordinal))
                {
                    _logger.LogDebug(
                        "Descoberta por período do D365: {Key} é do estabelecimento {Branch}, mas com o CNPJ {Cnpj}, e não {Company}. Fica fora.",
                        reference.NaturalKey, reference.Metadata.BranchCode, reference.Metadata.CompanyCode, criteria.Company);
                    continue;
                }

                found.Add(reference with { Origin = Origin });
            }

            if (rows.Count < settings.PageSize)
            {
                return found;
            }

            after = rows[^1].FiscalDocumentRecId;
        }
    }

    /// <summary>
    /// A nota pela chave natural (<c>dataAreaId|Voucher</c>), para o reprocesso (D5). Outra forma de chave não é do D365, e dá
    /// <c>null</c> sem rede. O <c>Voucher</c> não lidera índice (ADR-0025): é uma varredura por clique, aceitável para um
    /// reprocesso manual.
    /// </summary>
    public async Task<DocumentReference?> FindByKeyAsync(string tenantId, string naturalKey, CancellationToken ct = default)
    {
        int bar = naturalKey.IndexOf('|', StringComparison.Ordinal);
        if (bar <= 0 || bar == naturalKey.Length - 1)
        {
            return null;
        }

        string company = naturalKey[..bar], voucher = naturalKey[(bar + 1)..];
        D365InboundSettings settings = await SettingsAsync(tenantId, ct);
        var connection = new D365Connection(tenantId, settings.Url, settings.Auth);

        string filter = $"dataAreaId eq '{Quote(company)}' and Voucher eq '{Quote(voucher)}'";
        var url = new Uri($"{Base(settings)}?{Query(filter, top: 2, orderByRecId: false)}");
        List<D365HeaderRow> rows = await D365ReadFailures.GuardAsync(() => PageAsync(url, connection, ct), Read, tenantId, _logger, ct);

        return rows.Count switch
        {
            0 => null,
            1 => D365HeaderReference.Map(rows[0], tenantId, settings, _logger) is { } reference ? reference with { Origin = Origin } : null,
            _ => throw new OriginUnavailableException(
                $"A chave {naturalKey} tem mais de um documento no F&O, e o reprocesso não sabe qual buscar."),
        };
    }

    private async Task<D365InboundSettings> SettingsAsync(string tenantId, CancellationToken ct)
    {
        // Settings primeiro: a configuração inválida falha sem tocar a rede.
        TenantConnectorProfile profile = await _profiles.GetAsync(tenantId, ct)
            ?? throw new ConnectorSettingsException($"Tenant '{tenantId}' sem perfil de conector.");
        return D365InboundSettings.Parse(profile.InboundSettings);
    }

    private async Task<List<D365HeaderRow>> PageAsync(Uri url, D365Connection connection, CancellationToken ct)
    {
        using HttpResponseMessage response = await _client.SendAsync(url, connection, ct);
        D365HeaderPage page = await response.Content.ReadFromJsonAsync<D365HeaderPage>(ct)
            ?? throw new InvalidOperationException("Resposta vazia do OData do F&O.");
        return page.Value ?? [];
    }

    private static Uri BuildUrl(
        D365InboundSettings settings, DateOnly first, DateOnly last, IReadOnlyList<D365Establishment>? scope, string? number, long? after)
    {
        // Os limites às 00:00:00Z e às 23:59:59Z cobrem o FiscalDocumentDate, que o OData devolve às 12:00Z, sem converter
        // fuso. Verificado contra o fiscosysdev em 2026-10-01 (tarefa 1.1).
        var terms = new List<string>
        {
            $"FiscalDocumentDate ge {Day(first)}T00:00:00Z",
            $"FiscalDocumentDate le {Day(last)}T23:59:59Z",
        };

        if (scope is not null)
        {
            terms.Add("(" + string.Join(" or ", scope.Select(e => $"(dataAreaId eq '{Quote(e.DataAreaId)}' and FiscalEstablishment eq '{Quote(e.Code)}')")) + ")");
        }
        else if (settings.Companies.Count > 0)
        {
            terms.Add("(" + string.Join(" or ", settings.Companies.Select(c => $"dataAreaId eq '{Quote(c)}'")) + ")");
        }

        if (number is not null)
        {
            terms.Add($"FiscalDocumentNumber eq '{Quote(number)}'");
        }

        if (after is { } recId)
        {
            terms.Add($"FiscalDocumentRecId gt {recId.ToString(CultureInfo.InvariantCulture)}");
        }

        return new Uri($"{Base(settings)}?{Query(string.Join(" and ", terms), settings.PageSize, orderByRecId: true)}");
    }

    private static string Query(string filter, int top, bool orderByRecId)
    {
        var query = new StringBuilder()
            .Append("cross-company=true")
            .Append("&$select=").Append(Uri.EscapeDataString(D365HeaderReference.Select));
        if (orderByRecId)
        {
            query.Append("&$orderby=FiscalDocumentRecId");
        }

        return query
            .Append("&$top=").Append(top.ToString(CultureInfo.InvariantCulture))
            .Append("&$filter=").Append(Uri.EscapeDataString(filter))
            .ToString();
    }

    private static string Base(D365InboundSettings settings) => $"{settings.Url.GetLeftPart(UriPartial.Authority)}/{D365HeaderReference.EntitySet}";

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
