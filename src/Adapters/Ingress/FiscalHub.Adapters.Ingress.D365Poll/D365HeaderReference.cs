using System.Globalization;
using System.Text.Json.Serialization;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// O cabeçalho da <c>FSFiscalDocumentBRs</c> como a descoberta o lê, e a referência que ele vira. É um lugar só para o feed
/// de mudanças e para a descoberta por período (change erp-company-directory-and-card-filters, D6): o mesmo cabeçalho dá a
/// mesma chave natural, o mesmo locator e o mesmo grupo pelos dois caminhos, por construção, e a nota cai no mesmo registro
/// e no mesmo card.
/// </summary>
internal static class D365HeaderReference
{
    public const string EntitySet = "data/FSFiscalDocumentBRs";

    // O que a referência, o grupo da nota e a paginação precisam. Não entra na impressão de conteúdo (o canônico é feito
    // das respostas da montagem): ampliar este $select não sobe o D365Canonicalizer.Version.
    public const string Select = "dataAreaId,Voucher,Model,Direction,Status,FiscalDocumentNumber,FiscalDocumentSeries,FiscalDocumentDate,FiscalEstablishmentCNPJCPF,FiscalEstablishment,SysModifiedDateTime,FiscalDocumentRecId";

    /// <summary>
    /// A referência do cabeçalho, ou <c>null</c> quando ele não vira referência: sem empresa ou voucher, ou com o modelo fora
    /// do mapa do tenant. Registro ruim não interrompe a leitura: aviso e segue (falha isolada por documento).
    /// </summary>
    public static DocumentReference? Map(D365HeaderRow row, string tenantId, D365InboundSettings settings, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(row.DataAreaId) || string.IsNullOrWhiteSpace(row.Voucher))
        {
            logger.LogWarning(
                "Cabeçalho do D365 sem empresa ou voucher ficou fora da fila (tenant {Tenant}, empresa '{Company}', voucher '{Voucher}', modelo '{Model}', RecId {RecId}).",
                tenantId, row.DataAreaId, row.Voucher, row.Model, row.FiscalDocumentRecId);
            return null;
        }

        if (row.Model is null || !settings.ModelTypes.TryGetValue(row.Model, out DocumentType type))
        {
            logger.LogWarning(
                "Modelo '{Model}' fora do mapa do tenant {Tenant}: empresa {Company}, voucher {Voucher} ficou fora da fila.",
                row.Model, tenantId, row.DataAreaId, row.Voucher);
            return null;
        }

        return new DocumentReference
        {
            TenantId = tenantId,
            Type = type,
            NaturalKey = $"{row.DataAreaId}|{row.Voucher}",
            // Contrato com a montagem (ADR-0025): o RecId é a chave primária do cabeçalho — o Voucher não lidera nenhum
            // índice da FiscalDocument_BR. O Voucher segue na NaturalKey, que a montagem confere.
            Locator = $"d365/{Uri.EscapeDataString(row.DataAreaId)}/{row.FiscalDocumentRecId.ToString(CultureInfo.InvariantCulture)}",
            Trigger = IngestionTrigger.Event,
            Metadata = Group(row),
        };
    }

    /// <summary>
    /// O grupo da nota, lido do mesmo registro, pelas mesmas leituras da montagem (<see cref="D365HeaderValues"/>): o
    /// estabelecimento próprio e o dia fiscal, sem valor padrão e sem conversão de fuso. A data inválida falha a leitura,
    /// como um <c>SysModifiedDateTime</c> inválido.
    /// </summary>
    private static DocumentMetadata Group(D365HeaderRow row) => new()
    {
        CompanyCode = D365HeaderValues.TaxId(row.FiscalEstablishmentCnpjCpf ?? string.Empty),
        BranchCode = row.FiscalEstablishment ?? string.Empty,
        ReferenceDate = D365HeaderValues.FiscalDay(row.FiscalDocumentDate),
        DocumentNumber = row.FiscalDocumentNumber ?? string.Empty,
        DocumentModel = row.Model!,   // o Map só chega aqui com o modelo no mapa do tenant
    };
}

/// <summary>Uma página da <c>FSFiscalDocumentBRs</c> — contrato OData preso ao adapter.</summary>
internal sealed record D365HeaderPage
{
    [JsonPropertyName("value")]
    public List<D365HeaderRow>? Value { get; init; }
}

/// <summary>Um cabeçalho da <c>FSFiscalDocumentBRs</c>, com os campos do <see cref="D365HeaderReference.Select"/>.</summary>
internal sealed record D365HeaderRow
{
    [JsonPropertyName("dataAreaId")]
    public string? DataAreaId { get; init; }

    [JsonPropertyName("Voucher")]
    public string? Voucher { get; init; }

    [JsonPropertyName("Model")]
    public string? Model { get; init; }

    [JsonPropertyName("FiscalDocumentNumber")]
    public string? FiscalDocumentNumber { get; init; }

    /// <summary>O dia fiscal, como o OData devolve um campo de data (<c>yyyy-MM-ddT12:00:00Z</c>).</summary>
    [JsonPropertyName("FiscalDocumentDate")]
    public string? FiscalDocumentDate { get; init; }

    [JsonPropertyName("FiscalEstablishmentCNPJCPF")]
    public string? FiscalEstablishmentCnpjCpf { get; init; }

    [JsonPropertyName("FiscalEstablishment")]
    public string? FiscalEstablishment { get; init; }

    /// <summary>Guardado como texto: é o literal que volta na âncora do keyset do feed.</summary>
    [JsonPropertyName("SysModifiedDateTime")]
    public string? SysModifiedDateTime { get; init; }

    [JsonPropertyName("FiscalDocumentRecId")]
    public long FiscalDocumentRecId { get; init; }
}
