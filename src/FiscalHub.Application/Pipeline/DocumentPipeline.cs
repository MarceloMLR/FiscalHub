using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Núcleo da esteira: processa um documento de ponta a ponta falando apenas pelas portas, sem
/// conhecer fila, Azure ou plataforma de destino. A casca de infraestrutura (gatilho do Service
/// Bus) apenas chama <see cref="ProcessAsync"/>; o retry e a DLQ ficam por conta do transporte.
/// </summary>
public sealed class DocumentPipeline<TDocument>
{
    private readonly IInboundSource<TDocument> _source;
    private readonly IComplianceDispatcher<TDocument> _dispatcher;
    private readonly IProcessingStore _store;

    public DocumentPipeline(
        IInboundSource<TDocument> source,
        IComplianceDispatcher<TDocument> dispatcher,
        IProcessingStore store)
    {
        _source = source;
        _dispatcher = dispatcher;
        _store = store;
    }

    /// <summary>
    /// Processa um documento: verifica idempotência, busca o conteúdo na origem, envia ao destino
    /// e registra o resultado. Qualquer falha propaga como exceção, deixando o retry/DLQ a cargo
    /// do transporte; a checagem de idempotência torna a reentrega segura.
    /// </summary>
    public async Task ProcessAsync(
        DocumentReference reference, DispatchContext context, CancellationToken ct = default)
    {
        if (await _store.AlreadySubmittedAsync(reference.TenantId, reference.NaturalKey, ct))
            return;

        TDocument document = await _source.FetchAsync(reference, ct);

        // Validação de integração (completude, mapeabilidade) entra aqui — fatia futura.

        IntegrationReceipt receipt = await _dispatcher.SubmitAsync(document, context, ct);

        await _store.RecordSubmissionAsync(reference, receipt, ct);
    }
}
