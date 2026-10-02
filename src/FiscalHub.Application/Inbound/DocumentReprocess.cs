using FiscalHub.Application.Auth;

namespace FiscalHub.Application.Inbound;

/// <summary>O desfecho do pedido de reprocesso.</summary>
public enum ReprocessStatus
{
    /// <summary>A nota foi achada na origem e reenfileirada com o gatilho manual.</summary>
    Queued,

    /// <summary>A nota é de outro tenant: a resposta é a de "não encontrada", sem confirmar que ela existe.</summary>
    OtherTenant,

    /// <summary>Nenhuma origem do tenant tem mais a nota.</summary>
    NotInOrigin,
}

/// <summary>
/// O reprocesso de uma nota (change erp-company-directory-and-card-filters, D5): a chave vai às descobertas do tenant, em
/// ordem — a do adapter de entrada primeiro, e o catálogo local de desenvolvimento depois —, e a primeira que acha a nota a
/// reenfileira com o gatilho manual, que fura a idempotência (ADR-0016). Dá certo sem guardar a origem no registro do
/// documento porque as formas de chave são disjuntas (<c>empresa|voucher</c> contra a chave de acesso de 44 dígitos), e o
/// D365 recusa a outra forma sem rede. A fila é a de descoberta, a do coletor (D7), para a cópia do reprocesso não correr em
/// paralelo com a do coletor. O reprocesso aceito é contado (<see cref="IReprocessLog"/>), e o modal mostra a contagem.
/// </summary>
public sealed class DocumentReprocess
{
    private readonly DocumentDiscoveryResolver _discoveries;
    private readonly IDocumentQueue _queue;
    private readonly IReprocessLog _log;
    private readonly ITenantContext _tenant;

    public DocumentReprocess(DocumentDiscoveryResolver discoveries, IDocumentQueue queue, IReprocessLog log, ITenantContext tenant)
    {
        _discoveries = discoveries;
        _queue = queue;
        _log = log;
        _tenant = tenant;
    }

    public async Task<ReprocessStatus> ReprocessAsync(string tenantId, string naturalKey, CancellationToken ct = default)
    {
        if (!string.Equals(tenantId, _tenant.TenantId, StringComparison.Ordinal))
        {
            return ReprocessStatus.OtherTenant;   // não confirma existência de nota de outro tenant (ADR-0028)
        }

        foreach (IDocumentDiscovery discovery in await _discoveries.CandidatesAsync(tenantId, ct))
        {
            if (await discovery.FindByKeyAsync(tenantId, naturalKey, ct) is { } reference)
            {
                await _queue.EnqueueAsync(reference with { Trigger = IngestionTrigger.Manual }, ct);
                await _log.RecordAsync(tenantId, naturalKey, ct);   // contado só depois de reenfileirar
                return ReprocessStatus.Queued;
            }
        }

        return ReprocessStatus.NotInOrigin;
    }
}
