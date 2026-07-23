using FiscalHub.Application.Inbound;
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

    public XmlGoodsInvoiceSource(IBlobReader blobReader, NfeXmlParser parser)
    {
        _blobReader = blobReader;
        _parser = parser;
    }

    public string Origin => "Xml";

    public async Task<GoodsInvoice> FetchAsync(DocumentReference reference, CancellationToken ct = default)
    {
        string xml = await _blobReader.ReadTextAsync(reference.Locator, ct);
        return _parser.Parse(xml);
    }
}
