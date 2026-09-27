using System.Globalization;
using System.Text;
using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Adapter de entrada do D365 F&amp;O: o fetch do claim-check (ADR-0025). Pelo Locator
/// <c>d365/&lt;empresa&gt;/&lt;RecId&gt;</c>, lê o cabeçalho, confere chave, modelo e status, e monta a NF-e em 4 GETs por
/// entidade — cabeçalho, linhas, impostos (dois termos: de linha e de encargo), encargos —, mais a contábil do
/// voucher só quando há <c>ImportTax</c> zerado. O JSON canônico das respostas é a foto da fonte e a impressão de
/// conteúdo; cadastros (endereço, cidade) vêm do cache e ficam fora da impressão.
/// </summary>
internal sealed class D365GoodsInvoiceSource : IInboundSource<GoodsInvoice>
{
    // $select do design D5 (add-d365-document-assembly), ampliado pelo D12 (connector-not-validator): o que a montagem
    // lê, mais as chaves — e o limite do que entra no hash. Mudou? Suba D365Canonicalizer.Version.
    public const string HeaderSelect = "FiscalDocumentRecId,dataAreaId,Voucher,Model,Status,Direction,FiscalDocumentIssuer,AccessKey,FiscalDocumentSeries,FiscalDocumentNumber,FiscalDocumentDate,FiscalDocumentDateTime,FiscalEstablishmentCNPJCPF,FiscalEstablishmentName,FiscalEstablishmentIE,FiscalEstablishmentPostalAddress,ThirdPartyCNPJCPF,ThirdPartyName,ThirdPartyIE,ThirdPartyPostalAddress,TotalAmount,TotalGoodsAmount,AccountingDate";
    public const string LineSelect = "FiscalDocumentLineRecId,FiscalDocumentRecId,LineNum,ItemId,Description,FiscalClassification,CFOP,Quantity,UnitPrice,LineAmount,Unit,AccountingAmount,Origin";
    public const string TaxSelect = "FiscalDocumentTaxTransRecId,FiscalDocumentLineRecId,FiscalDocumentMiscChargeRecId,TaxTransRecId,FiscalTaxType,TaxationCode,TaxBaseAmount,TaxBaseAmountExempt,TaxBaseAmountOther,TaxValue,TaxAmount,RetainedTax";
    public const string ChargeSelect = "FiscalDocumentMiscChargeRecId,FiscalDocumentLineRecId,ChargeNum,MiscChargeType,Amount,Txt";
    public const string TaxTransSelect = "TaxTransRecId,Voucher,TaxType,TaxBaseAmount,TaxValue,TaxAmount";
    public const string PostalAddressSelect = "PostalAddressRecId,CityRecId,Street,StreetNumber,DistrictName,ZipCode";
    private const string CitySelect = "AddressCityRecId,IBGECode";

    private readonly D365ODataClient _client;
    private readonly IConnectorProfileStore _profiles;
    private readonly D365ReferenceDataCache _cache;
    private readonly IProcessingTrace _trace;
    private readonly ILogger<D365GoodsInvoiceSource> _logger;

    public D365GoodsInvoiceSource(
        HttpClient http,
        IConnectorProfileStore profiles,
        ID365TokenProvider tokens,
        D365ChangeFeedOptions options,
        D365ReferenceDataCache cache,
        IProcessingTrace trace,
        TimeProvider clock,
        ILogger<D365GoodsInvoiceSource> logger)
    {
        _client = new D365ODataClient(http, tokens, options, clock);
        _profiles = profiles;
        _cache = cache;
        _trace = trace;
        _logger = logger;
    }

    public string Origin => D365ChangeFeed.OriginName;

    /// <summary>
    /// O formato <c>d365/{empresa}/{recId}</c>. O locator do D365 é lido no ERP do próprio tenant da referência, com a
    /// credencial dele, então o formato é a regra inteira (ADR-0028).
    /// </summary>
    public string? CheckLocator(DocumentReference reference)
    {
        try
        {
            D365DocumentLocator.Parse(reference.Locator);
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    public async Task<FetchResult<GoodsInvoice>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
    {
        // Locator e settings primeiro: formato ou configuração inválidos falham sem tocar a rede.
        D365DocumentLocator locator = D365DocumentLocator.Parse(reference.Locator);
        TenantConnectorProfile profile = await _profiles.GetAsync(reference.TenantId, ct)
            ?? throw new ConnectorSettingsException($"Tenant '{reference.TenantId}' sem perfil de conector.");
        D365InboundSettings settings = D365InboundSettings.Parse(profile.InboundSettings);
        var connection = new D365Connection(reference.TenantId, settings.Url, settings.Auth);
        string rec = locator.FiscalDocumentRecId.ToString(CultureInfo.InvariantCulture);

        JsonElement header = await HeaderAsync(settings, connection, locator, reference, ct);

        IReadOnlyList<JsonElement> lines = await GetAsync(settings, connection, "FSFiscalDocumentLineBRs", LineSelect, $"FiscalDocumentRecId eq {rec}", ct);
        IReadOnlyList<JsonElement> taxes = await GetAsync(settings, connection, "FSFiscalDocumentTaxTransBRs", TaxSelect,
            $"FiscalDocumentRecId eq {rec} or MiscChargeFiscalDocumentRecId eq {rec}", ct);
        IReadOnlyList<JsonElement> charges = await GetAsync(settings, connection, "FSFiscalDocumentMiscChargeBRs", ChargeSelect, $"FiscalDocumentRecId eq {rec}", ct);

        // Complemento contábil só quando há imposto elegível (ImportTax zerado com ponte): uma consulta por voucher e
        // empresa, casada em memória. Só as linhas casadas entram no canônico e na montagem.
        IReadOnlySet<long> bridges = D365GoodsInvoiceAssembler.ComplementBridges(taxes);
        IReadOnlyList<JsonElement> accounting = [];
        if (bridges.Count > 0)
        {
            string voucher = header.GetProperty("Voucher").GetString()!;
            IReadOnlyList<JsonElement> voucherRows = await GetAsync(settings, connection, "FSTaxTransBRs", TaxTransSelect,
                $"Voucher eq '{Literal(voucher)}' and dataAreaId eq '{Literal(locator.Company)}'", ct);
            accounting = [.. voucherRows.Where(r => r.TryGetProperty("TaxTransRecId", out JsonElement id) && bridges.Contains(id.GetInt64()))];
        }

        var rows = new D365DocumentRows(header, lines, taxes, charges, accounting);

        // Foto da fonte ANTES do mapeamento (ADR-0006): uma falha de montagem ou divergência deixa os dois lados salvos.
        // É exatamente o que a impressão cobre — dá para recalculá-la a partir da foto.
        string canonical = D365Canonicalizer.Canonicalize(rows);
        await _trace.SaveSourceAsync(reference.TenantId, reference.NaturalKey, canonical, "json", ct);

        var referenceData = new D365PartyReferenceData(
            await PlaceAsync(settings, connection, reference.TenantId, header, "FiscalEstablishmentPostalAddress", ct),
            await PlaceAsync(settings, connection, reference.TenantId, header, "ThirdPartyPostalAddress", ct));

        return new FetchResult<GoodsInvoice>
        {
            Document = D365GoodsInvoiceAssembler.Assemble(rows, referenceData),
            ContentHash = ContentFingerprint.Of(canonical),
        };
    }

    /// <summary>
    /// Lê o cabeçalho pela chave primária e confere: existe (vazio é falha, nunca "ignorado" — com cross-company, falta de
    /// acesso à empresa também devolve vazio), a chave natural bate, modelo 55 e status autorizado. Fora do escopo →
    /// <see cref="DocumentOutOfScopeException"/>, e nada mais é consultado.
    /// </summary>
    private async Task<JsonElement> HeaderAsync(
        D365InboundSettings settings, D365Connection connection, D365DocumentLocator locator, DocumentReference reference, CancellationToken ct)
    {
        string rec = locator.FiscalDocumentRecId.ToString(CultureInfo.InvariantCulture);
        IReadOnlyList<JsonElement> found = await GetAsync(settings, connection, "FSFiscalDocumentBRs", HeaderSelect, $"FiscalDocumentRecId eq {rec}", ct);
        if (found.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cabeçalho não encontrado no F&O: empresa '{locator.Company}', FiscalDocumentRecId {rec} (documento '{reference.NaturalKey}'). "
                + "Resultado vazio também é o sintoma de o usuário de integração ter perdido acesso à empresa — conferir a permissão antes de concluir que a nota sumiu.");
        }

        JsonElement header = found[0];
        string key = $"{header.GetProperty("dataAreaId").GetString()}|{header.GetProperty("Voucher").GetString()}";
        if (!string.Equals(key, reference.NaturalKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Chave natural não confere para o RecId {rec}: a referência diz '{reference.NaturalKey}', o cabeçalho lido é '{key}'.");
        }

        string model = header.GetProperty("Model").GetString() ?? string.Empty;
        if (model != "55")
        {
            throw new DocumentOutOfScopeException($"ignorado: tipo fora do escopo (modelo {model})");
        }

        string status = header.GetProperty("Status").GetString() ?? string.Empty;
        if (status != "Approved")
        {
            throw new DocumentOutOfScopeException($"ignorado: status {status} fora do escopo (só nota autorizada é despachada)");
        }

        return header;
    }

    /// <summary>
    /// Endereço (RecId do cabeçalho) → logradouro, número, bairro e CEP, e → cidade → código IBGE, pelo cache do tenant.
    /// FK vazia ou não encontrado → endereço e município ausentes.
    /// </summary>
    private async Task<D365PartyPlace> PlaceAsync(
        D365InboundSettings settings, D365Connection connection, string tenantId, JsonElement header, string addressField, CancellationToken ct)
    {
        long addressRecId = header.TryGetProperty(addressField, out JsonElement a) && a.ValueKind == JsonValueKind.Number ? a.GetInt64() : 0;
        JsonElement? address = await _cache.GetAsync(tenantId, "FSPostalAddressBRs", addressRecId,
            token => FirstAsync(settings, connection, "FSPostalAddressBRs", PostalAddressSelect, $"PostalAddressRecId eq {addressRecId}", token), ct);
        if (address is not { } row)
        {
            if (addressRecId != 0)
            {
                _logger.LogWarning("Endereço {Address} ({Field}) não encontrado no F&O; o endereço e o município da parte ficam ausentes.", addressRecId, addressField);
            }

            return D365PartyPlace.None;
        }

        long cityRecId = row.TryGetProperty("CityRecId", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
        JsonElement? city = await _cache.GetAsync(tenantId, "FSAddressCityBRs", cityRecId,
            token => FirstAsync(settings, connection, "FSAddressCityBRs", CitySelect, $"AddressCityRecId eq {cityRecId}", token), ct);

        string? ibge = city is { } found && found.TryGetProperty("IBGECode", out JsonElement code) ? code.GetString() : null;
        return new D365PartyPlace(string.IsNullOrEmpty(ibge) ? null : ibge, D365PartyPlace.AddressOf(row));
    }

    private async Task<JsonElement?> FirstAsync(
        D365InboundSettings settings, D365Connection connection, string entitySet, string select, string filter, CancellationToken ct)
    {
        IReadOnlyList<JsonElement> rows = await GetAsync(settings, connection, entitySet, select, filter, ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    private Task<IReadOnlyList<JsonElement>> GetAsync(
        D365InboundSettings settings, D365Connection connection, string entitySet, string select, string filter, CancellationToken ct)
    {
        var query = new StringBuilder()
            .Append("cross-company=true")
            .Append("&$select=").Append(Uri.EscapeDataString(select))
            .Append("&$filter=").Append(Uri.EscapeDataString(filter));

        return _client.GetAllAsync(new Uri($"{settings.Url.GetLeftPart(UriPartial.Authority)}/data/{entitySet}?{query}"), connection, ct);
    }

    private static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
