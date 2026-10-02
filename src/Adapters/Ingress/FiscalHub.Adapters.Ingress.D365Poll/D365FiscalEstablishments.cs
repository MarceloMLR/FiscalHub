using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>Um estabelecimento do cadastro do F&amp;O, com o CNPJ já sem a pontuação (a empresa do hub).</summary>
internal sealed record D365Establishment(string DataAreaId, string Code, string Cnpj, string Name);

/// <summary>
/// O cadastro de estabelecimentos fiscais do F&amp;O, pela <c>FiscalEstablishments</c>, a entidade padrão da Microsoft
/// (change erp-company-directory-and-card-filters, D1). Serve o diretório e a descoberta por período, para os dois
/// resolverem a empresa e a filial pela mesma leitura. Não é uma <c>FS*</c>: as nossas existem porque a Microsoft não
/// publica entidade sobre a <c>FiscalDocument_BR</c>, e esta ela publica, com nome fixo. Exige o privilégio padrão
/// <c>FiscalEstablishmentEntityView</c> na role do pacote. O cadastro é pequeno e não é janela móvel: o
/// <c>@odata.nextLink</c> é seguido, como nas linhas de um documento lançado, e não por keyset, como no feed.
/// </summary>
internal sealed class D365FiscalEstablishments
{
    public const string EntitySet = "data/FiscalEstablishments";

    /// <summary>Os quatro campos que o hub usa. IE, CCM e o grupo ficam fora de propósito (d365/07, passo 5).</summary>
    public const string Select = "dataAreaId,FiscalEstablishmentId,CNPJ,Name";

    public static readonly D365Read Read = new("do cadastro de estabelecimentos", "FiscalEstablishments", "FiscalEstablishmentEntityView");

    private readonly D365ODataClient _client;
    private readonly ILogger _logger;

    public D365FiscalEstablishments(D365ODataClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>Os estabelecimentos das empresas do perfil (todos os que a credencial enxerga, sem <c>companies</c>).</summary>
    public async Task<IReadOnlyList<D365Establishment>> ReadAsync(string tenantId, D365InboundSettings settings, CancellationToken ct)
    {
        var connection = new D365Connection(tenantId, settings.Url, settings.Auth);
        IReadOnlyList<JsonElement> rows = await D365ReadFailures.GuardAsync(
            () => _client.GetAllAsync(BuildUrl(settings), connection, ct), Read, tenantId, _logger, ct);

        var establishments = new List<D365Establishment>(rows.Count);
        foreach (JsonElement row in rows)
        {
            string company = Str(row, "dataAreaId"), code = Str(row, "FiscalEstablishmentId");
            string cnpj = D365HeaderValues.TaxId(Str(row, "CNPJ"));

            // Sem CNPJ ou sem código não há chave de grupo: fica fora, com aviso, e a leitura segue.
            if (cnpj.Length == 0 || code.Length == 0)
            {
                _logger.LogWarning(
                    "Estabelecimento do D365 sem CNPJ ou sem código ficou fora do diretório (tenant {Tenant}, empresa '{Company}', código '{Code}').",
                    tenantId, company, code);
                continue;
            }

            establishments.Add(new D365Establishment(company, code, cnpj, Str(row, "Name")));
        }

        return establishments;
    }

    private static Uri BuildUrl(D365InboundSettings settings)
    {
        var query = new StringBuilder()
            .Append("cross-company=true")
            .Append("&$select=").Append(Uri.EscapeDataString(Select));

        if (settings.Companies.Count > 0)
        {
            string companies = string.Join(" or ", settings.Companies.Select(c => $"dataAreaId eq '{c.Replace("'", "''", StringComparison.Ordinal)}'"));
            query.Append("&$filter=").Append(Uri.EscapeDataString(companies));
        }

        return new Uri($"{settings.Url.GetLeftPart(UriPartial.Authority)}/{EntitySet}?{query}");
    }

    private static string Str(JsonElement row, string name)
        => row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
}
