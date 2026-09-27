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
    private readonly string _inboxContainer;

    public XmlGoodsInvoiceSource(
        IBlobReader blobReader, NfeXmlParser parser, IProcessingTrace trace, string inboxContainer = XmlLocator.DefaultInboxContainer)
    {
        _blobReader = blobReader;
        _parser = parser;
        _trace = trace;
        _inboxContainer = inboxContainer;
    }

    public string Origin => "Xml";

    public string? CheckLocator(DocumentReference reference) => XmlLocator.Check(reference, _inboxContainer);

    public async Task<FetchResult<GoodsInvoice>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
    {
        // A ingestão manual já conferiu; aqui a regra vale para qualquer caminho até a esteira (drop, descoberta,
        // reprocesso, mensagem na fila). Só chega a recusar se algo contornou a entrada — daí exceção, e não desfecho.
        if (CheckLocator(reference) is { } problem)
        {
            throw new ArgumentException($"Locator recusado: {problem}", nameof(reference));
        }

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
