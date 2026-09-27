using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Roteamento por tipo (ADR-0025): só a NF-e de mercadoria tem esteira hoje. NFS-e e CT-e saem como
/// "ignorado: tipo fora do escopo", gravado e visível, sem tocar a origem. Um source que constate, na busca,
/// que o documento saiu do escopo (<see cref="DocumentOutOfScopeException"/>) também vira "ignorado" — a busca
/// é o primeiro passo da esteira, então nada foi gravado antes. Qualquer outra falha segue para o transporte.
/// </summary>
public sealed class DocumentRouter : IDocumentRouter
{
    private readonly IDocumentPipeline<GoodsInvoice> _goods;
    private readonly IProcessingStore _store;

    public DocumentRouter(IDocumentPipeline<GoodsInvoice> goods, IProcessingStore store)
    {
        _goods = goods;
        _store = store;
    }

    public async Task RouteAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default)
    {
        if (reference.Type != DocumentType.GoodsInvoice55)
        {
            await _store.RecordIgnoredAsync(reference, $"ignorado: tipo fora do escopo ({reference.Type})", ct);
            return;
        }

        try
        {
            await _goods.ProcessAsync(reference, context, ct);
        }
        catch (DocumentOutOfScopeException ex)
        {
            await _store.RecordIgnoredAsync(reference, ex.Reason, ct);
        }
    }
}
