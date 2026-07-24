using FiscalHub.Application.Inbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>
/// Adapter de entrada por XML: o fetch do claim-check. Lê o XML da NF-e no Blob (pelo Locator da
/// referência) e o converte na <see cref="GoodsInvoice"/> via <see cref="NfeXmlParser"/>.
/// </summary>
internal sealed class XmlGoodsInvoiceSource : IInboundSource<GoodsInvoice>
{
    private readonly IBlobReader _blobReader;
    private readonly NfeXmlParser _parser;
    private readonly IProcessingTrace _trace;

    public XmlGoodsInvoiceSource(IBlobReader blobReader, NfeXmlParser parser, IProcessingTrace trace)
    {
        _blobReader = blobReader;
        _parser = parser;
        _trace = trace;
    }

    public string Origin => "Xml";

    public async Task<FetchResult<GoodsInvoice>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
    {
        string xml = await _blobReader.ReadTextAsync(reference.Locator, ct);

        // Foto da fonte crua (ADR-0006): o que chegou do cliente, antes de qualquer tradução. Fica
        // antes do parse de propósito — se o parse falhar, o XML problemático já está salvo.
        await _trace.SaveSourceAsync(reference.TenantId, reference.NaturalKey, xml, "xml", ct);

        // Impressão do cru (ADR-0016): decide idempotência por conteúdo lá na esteira.
        return new FetchResult<GoodsInvoice>
        {
            Document = _parser.Parse(xml),
            ContentHash = ContentFingerprint.Of(xml),
        };
    }
}
