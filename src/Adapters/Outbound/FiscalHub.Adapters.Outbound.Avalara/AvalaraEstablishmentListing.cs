using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>
/// A listagem de estabelecimentos da Avalara (change <c>platform-establishment-resolution</c>, D6 e D7): as empresas da conta
/// do tenant e, empresa a empresa, os contribuintes de cada uma — o <c>empresaId</c> é obrigatório no <c>/contribuinte</c>,
/// e não há como pedir um CNPJ na conta inteira. O payload leva o <c>codigoCIA</c> da empresa e o <c>codigo</c> do
/// contribuinte, lidos como texto, como vieram.
/// <list type="bullet">
///   <item><b>As duas formas de uma lista</b> (change <c>platform-listing-shape</c>): o array puro e o objeto com o array em
///   <c>value</c>, nas duas listas, decididas a cada resposta. O <c>/empresa</c> devolve o envelope com opções de query,
///   inclusive na página vazia, e o array sem elas, e a forma não pode depender de como o adapter monta a query. Qualquer
///   outra forma é recusa de contrato, que nomeia as propriedades recebidas, e nunca os valores.</item>
///   <item><b>A paginação, pelo cliente:</b> ordem estável pelo <c>$orderby</c>, <c>$top</c> fixo, o <c>$skip</c> somando os
///   itens <b>recebidos</b> (um servidor que limite a página só dá mais páginas, sem pular itens), e a parada só na página
///   vazia. A página igual à anterior (o <c>$skip</c> ignorado) e o teto de páginas são recusa de contrato.</item>
///   <item><b>Tudo ou nada:</b> qualquer falha, em qualquer página de qualquer empresa, lança, e nada parcial é devolvido. A
///   listagem parcial esconderia a duplicidade que estivesse justamente na empresa que falhou.</item>
///   <item><b>A credencial e o token do envio,</b> pela mesma seção do ambiente ativo. O corpo é redigido antes de virar
///   motivo, e o log diz só as contagens.</item>
/// </list>
/// </summary>
internal sealed class AvalaraEstablishmentListing : IPlatformEstablishmentListing
{
    private const string CompaniesSelect = "$select=empresaId,codigoCIA,descricao&$orderby=empresaId";
    private const string TaxpayersSelect = "$select=contribuinteId,codigo,cnpj&$orderby=contribuinteId";

    // Quantos nomes de propriedade a recusa da forma cita, antes do "e mais N".
    private const int MaxNamedProperties = 10;

    private readonly HttpClient _http;
    private readonly IAvalaraTokenProvider _tokens;
    private readonly AvalaraOptions _options;
    private readonly ILogger<AvalaraEstablishmentListing> _logger;

    public AvalaraEstablishmentListing(
        HttpClient http, IAvalaraTokenProvider tokens, IOptions<AvalaraOptions> options, ILogger<AvalaraEstablishmentListing> logger)
    {
        _http = http;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
    }

    public string Adapter => AvalaraServiceCollectionExtensions.AdapterName;

    public async Task<IReadOnlyList<PlatformEstablishment>> ListAsync(TenantConnectorProfile profile, CancellationToken ct = default)
    {
        AvalaraOutboundSettings settings = AvalaraOutboundSettings.Read(profile.TenantId, profile);

        var companies = new ListPath(
            "a listagem de empresas", "da listagem de empresas", _options.CompaniesPath, "Avalara:CompaniesPath", CompaniesSelect);
        (List<JsonElement> companyItems, int companyPages) = await ReadAllAsync(settings, companies, ct);

        var establishments = new List<PlatformEstablishment>();
        int taxpayerPages = 0;
        foreach (JsonElement company in companyItems)
        {
            string empresaId = Id(company, "empresaId")
                ?? throw Contract($"a listagem de empresas ({_options.CompaniesPath}) trouxe uma empresa sem empresaId, e sem ele os "
                    + "contribuintes dela não podem ser lidos. Seguir sem ela seria uma listagem parcial.");
            string? codigoCia = Text(company, "codigoCIA");
            string? descricao = Text(company, "descricao");

            var taxpayers = new ListPath(
                $"a listagem de contribuintes da empresa {empresaId}{(codigoCia is null ? string.Empty : $" ('{codigoCia}')")}",
                "da listagem de contribuintes", _options.TaxpayersPath, "Avalara:TaxpayersPath",
                $"empresaId={Uri.EscapeDataString(empresaId)}&{TaxpayersSelect}");
            (List<JsonElement> taxpayerItems, int pages) = await ReadAllAsync(settings, taxpayers, ct);
            taxpayerPages += pages;

            foreach (JsonElement taxpayer in taxpayerItems)
            {
                establishments.Add(new PlatformEstablishment(
                    TaxIdentifiers.Normalize(Text(taxpayer, "cnpj") ?? string.Empty), codigoCia, Text(taxpayer, "codigo"),
                    Id(taxpayer, "contribuinteId"), descricao));
            }
        }

        _logger.LogInformation(
            "Listagem de estabelecimentos da plataforma do tenant {Tenant} no ambiente {Environment}: {Companies} empresas em "
            + "{CompanyPages} páginas e {Taxpayers} contribuintes em {TaxpayerPages} páginas.",
            settings.TenantId, settings.Environment, companyItems.Count, companyPages, establishments.Count, taxpayerPages);
        return establishments;
    }

    // As páginas de uma lista, até a vazia: o $skip soma os itens recebidos, e não o $top.
    private async Task<(List<JsonElement> Items, int Pages)> ReadAllAsync(AvalaraOutboundSettings settings, ListPath list, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? previous = null;
        int skip = 0, previousSkip = 0;
        for (int page = 1; page <= _options.ListingMaxPages; page++)
        {
            JsonElement[] received = await PageAsync(settings, list, skip, ct);
            if (received.Length == 0)
            {
                return (items, page);
            }

            // Com a ordem estável, a plataforma que ignora o $skip devolve sempre a mesma página. Item a item, sem depender de
            // identificador, que pode faltar.
            string signature = string.Join('\u001f', received.Select(e => e.GetRawText()));
            if (signature == previous)
            {
                throw Contract($"{list.Label} ({list.Path}) devolveu em $skip={skip} a mesma página de $skip={previousSkip}: a "
                    + "plataforma não está respeitando o $skip, e a leitura não terminaria.");
            }

            previous = signature;
            previousSkip = skip;
            items.AddRange(received);
            skip += received.Length;
        }

        throw Contract($"{list.Label} ({list.Path}) não chegou à página vazia em {_options.ListingMaxPages} páginas, o teto "
            + "(Avalara:ListingMaxPages). O teto é em páginas: com um limite de página do servidor abaixo do $top, ele cobre "
            + "menos itens. Suba o Avalara:ListingMaxPages se a conta tiver mesmo tantos itens.");
    }

    private async Task<JsonElement[]> PageAsync(AvalaraOutboundSettings settings, ListPath list, int skip, CancellationToken ct)
    {
        var url = new Uri(settings.BaseUri, $"{list.Path}?{list.Query}&$top={_options.ListingPageSize}&$skip={skip}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AvalaraAccessToken token = await _tokens.GetTokenAsync(settings, ct);
        if (!token.IsNone)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        (string body, _) = SensitiveText.Redact(await response.Content.ReadAsStringAsync(ct), [token.Value]);
        int status = (int)response.StatusCode;

        switch (response.StatusCode)
        {
            // A credencial que envia pode não listar: retentar não muda permissão, e a saída é a tabela (ADR-0027 §7).
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.Unauthorized when token.IsFresh:
                throw AvalaraOutboundSettings.Rejected(
                    $"a plataforma negou {list.Label} ao tenant '{settings.TenantId}' no ambiente '{settings.Environment}' "
                    + $"(HTTP {status}{(status == 401 ? ", com token recém-emitido" : string.Empty)}): {PlatformMessage.Extract(body, status)}. "
                    + $"Sem a listagem, a tradução do estabelecimento vai em OutboundSettings.{settings.Environment}.establishments.");

            // Token do cache vencido ou revogado: descarta, e a próxima tentativa pede outro (retry nativo).
            case HttpStatusCode.Unauthorized:
                _tokens.Invalidate(token);
                throw new HttpRequestException(
                    $"A plataforma recusou o token em cache do tenant '{settings.TenantId}' no ambiente '{settings.Environment}' em "
                    + $"{list.Label} (HTTP 401); o token foi descartado, e a próxima tentativa pede outro.", null, HttpStatusCode.Unauthorized);

            case HttpStatusCode.NotFound:
                throw settings.PathNotFound(list.What, "GET", url, list.Option, list.Path,
                    body.Length == 0 ? null : PlatformMessage.Extract(body, status));
        }

        // Outro 4xx é recusa: retentar repetiria a mesma resposta. O 408 e o 429 seguem como indisponibilidade.
        if (status is >= 400 and < 500 and not (408 or 429))
        {
            throw Contract($"a plataforma recusou {list.Label} (HTTP {status}): {PlatformMessage.Extract(body, status)}");
        }

        response.EnsureSuccessStatusCode();   // 5xx, 408 e 429: o retry nativo e a dead-letter (ADR-0004)
        return Parse(body, list);
    }

    private static JsonElement[] Parse(string body, ListPath list)
    {
        JsonElement root;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Contract($"{list.Label} ({list.Path}) não respondeu com JSON.");
        }

        // A forma é desta resposta, e não da lista nem da página anterior. As outras propriedades do envelope, como o
        // @odata.nextLink, ficam de fora: a paginação continua sendo do hub.
        JsonElement array = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array => value,
            _ => throw Contract($"{list.Label} ({list.Path}) não veio em nenhuma das duas formas de lista, um array ou um objeto com o "
                + $"array em \"value\" (veio {Describe(root)})."),
        };

        JsonElement[] items = [.. array.EnumerateArray()];
        return items.Any(i => i.ValueKind != JsonValueKind.Object)
            ? throw Contract($"{list.Label} ({list.Path}) trouxe um item que não é objeto.")
            : items;
    }

    // Só texto JSON, e sem Trim: um número JSON é ausente, porque convertê-lo perderia os zeros à esquerda do texto.
    private static string? Text(JsonElement item, string name)
        => Property(item, name) is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text ? text : null;

    // O identificador como veio, número ou texto: o hub não o tipa.
    private static string? Id(JsonElement item, string name) => Property(item, name) switch
    {
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        { ValueKind: JsonValueKind.String } text when text.GetString() is { Length: > 0 } id => id,
        _ => null,
    };

    private static JsonElement? Property(JsonElement item, string name)
        => item.ValueKind == JsonValueKind.Object
            && item.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: not JsonValueKind.Undefined } property
                ? property.Value
                : null;

    // O que veio no lugar da lista: os nomes das propriedades de primeiro nível, e nunca os valores, que são resposta da
    // plataforma e podem ter dado de cliente. "error, message" e "value, que não é um array" levam a conclusões opostas.
    private static string Describe(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Kind(root);
        }

        string[] names = [.. root.EnumerateObject().Select(p => p.Name)];
        string described = names.Length switch
        {
            0 => "um objeto vazio",
            1 => $"um objeto com a propriedade {names[0]}",
            > MaxNamedProperties => $"um objeto com as propriedades {string.Join(", ", names.Take(MaxNamedProperties))} e mais {names.Length - MaxNamedProperties}",
            _ => $"um objeto com as propriedades {string.Join(", ", names)}",
        };

        return root.TryGetProperty("value", out JsonElement value) ? $"{described}, mas o value é {Kind(value)}, e não um array" : described;
    }

    private static string Kind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "um objeto",
        JsonValueKind.Array => "um array",
        JsonValueKind.String => "um texto",
        JsonValueKind.Number => "um número",
        JsonValueKind.True or JsonValueKind.False => "um booleano",
        _ => "null",
    };

    private static DispatchRejectedException Contract(string what) => new($"Contrato do destino: {what}");

    /// <summary>Uma lista da plataforma: como o motivo a nomeia, o caminho, a opção que o define e a query.</summary>
    private sealed record ListPath(string Label, string What, string Path, string Option, string Query);
}
