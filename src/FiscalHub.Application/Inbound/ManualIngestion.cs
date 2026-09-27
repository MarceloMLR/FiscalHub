using FiscalHub.Application.Auth;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Ingestão manual de um XML já no Blob (<c>POST /ingest</c>). O tenant é o de quem está logado, e nunca vem da
/// requisição (ADR-0028): um tenant no corpo gravaria documento fiscal em nome de outro cliente, que a esteira
/// montaria e despacharia do lado de lá. O locator passa pela regra da origem antes de enfileirar.
/// </summary>
public sealed class ManualIngestion
{
    private const string XmlOrigin = "Xml";

    private readonly IInboundSourceResolver<GoodsInvoice> _sources;
    private readonly IDocumentQueue _queue;
    private readonly ITenantContext _tenant;

    public ManualIngestion(IInboundSourceResolver<GoodsInvoice> sources, IDocumentQueue queue, ITenantContext tenant)
    {
        _sources = sources;
        _queue = queue;
        _tenant = tenant;
    }

    /// <summary>Enfileira a referência e devolve <c>null</c>; ou devolve a regra que recusou o locator, sem enfileirar.</summary>
    public async Task<string?> EnqueueAsync(string naturalKey, string locator, CancellationToken ct = default)
    {
        var reference = new DocumentReference
        {
            TenantId = _tenant.TenantId,
            Type = DocumentType.GoodsInvoice55,
            NaturalKey = naturalKey,
            Locator = locator,
            Origin = XmlOrigin,   // o locator é de um XML no Blob — vale mesmo para tenant cujo feed é outro ERP (ADR-0025)
        };

        IInboundSource<GoodsInvoice> source = await _sources.ResolveAsync(reference, ct);
        if (source.CheckLocator(reference) is { } problem)
        {
            return problem;
        }

        await _queue.EnqueueAsync(reference, ct);
        return null;
    }
}
