using System.Text.Json;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Núcleo da esteira: processa um documento de ponta a ponta falando apenas pelas portas, sem
/// conhecer fila, Azure ou plataforma de destino. A casca de infraestrutura (gatilho do Service
/// Bus) apenas chama <see cref="ProcessAsync"/>; o retry e a DLQ ficam por conta do transporte.
/// </summary>
public sealed class DocumentPipeline<TDocument> : IDocumentPipeline<TDocument>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IInboundSourceResolver<TDocument> _sources;
    private readonly IDocumentValidator<TDocument> _validator;
    private readonly IComplianceDispatcher<TDocument> _dispatcher;
    private readonly IProcessingStore _store;
    private readonly IProcessingTrace _trace;
    private readonly IDocumentMetadataExtractor<TDocument> _metadata;
    private readonly IOutboundDispatchSwitch _dispatch;

    /// <summary>
    /// O motivo gravado na nota que parou na chave de envio. É o que a tela mostra como situação dela, e segue o formato
    /// dos outros desfechos da esteira ("ignorado: …").
    /// </summary>
    public const string OutboundDispatchOffReason =
        "salva: o envio ao conector de saída está desligado nas configurações do tenant";

    public DocumentPipeline(
        IInboundSourceResolver<TDocument> sources,
        IDocumentValidator<TDocument> validator,
        IComplianceDispatcher<TDocument> dispatcher,
        IProcessingStore store,
        IProcessingTrace trace,
        IDocumentMetadataExtractor<TDocument> metadata,
        IOutboundDispatchSwitch dispatch)
    {
        _sources = sources;
        _validator = validator;
        _dispatcher = dispatcher;
        _store = store;
        _trace = trace;
        _metadata = metadata;
        _dispatch = dispatch;
    }

    /// <summary>
    /// Processa um documento: idempotência, busca, validação de integração, despacho e registro.
    /// Documento inválido é rejeitado (registrado com o motivo) e não é enviado; recusa permanente no despacho
    /// (<see cref="DispatchRejectedException"/>) também. Qualquer outra falha propaga como exceção, deixando o
    /// retry/DLQ a cargo do transporte.
    /// <para>O despacho é preparar e depois enviar, e a chave de envio do tenant decide entre os dois (ADR-0041):
    /// desligada, o payload de destino fica gravado e a nota termina em "Salva", sem requisição de envio.</para>
    /// </summary>
    public async Task ProcessAsync(
        DocumentReference reference, DispatchContext context, CancellationToken ct = default)
    {
        // A origem da referência escolhe o adapter (ADR-0025): o mesmo tenant pode ter XML e D365.
        IInboundSource<TDocument> source = await _sources.ResolveAsync(reference, ct);

        // Busca primeiro pra conhecer o conteúdo cru — é ele que decide a idempotência.
        FetchResult<TDocument> fetched = await source.FetchAsync(reference, ct);
        TDocument document = fetched.Document;

        // Idempotência por conteúdo (ADR-0016): já processei ESTE cru (mesmo hash, estado terminal)?
        // Evento redisparado com o cru idêntico é duplicata de entrega — ignora. Se o cliente
        // corrigiu a nota (ex.: nota de entrada com valor errado), o cru muda, o hash muda e ela
        // reintegra. Recarga manual (ADR-0015) fura de propósito.
        if (reference.Trigger != IngestionTrigger.Manual
            && await _store.AlreadyProcessedAsync(reference.TenantId, reference.NaturalKey, fetched.ContentHash, ct))
            return;

        // Foto do domínio (ADR-0006): o documento já no nosso modelo, antes de validar — assim até
        // uma nota rejeitada deixa registrado o que a gente entendeu dela.
        await _trace.SaveDomainAsync(reference.TenantId, reference.NaturalKey, JsonSerializer.Serialize(document, JsonOpts), ct);

        // Metadados de agrupamento (empresa/filial/data) + hash do cru na primeira passada — assim até
        // uma nota rejeitada aparece no grupo certo do dashboard.
        await _store.RecordMetadataAsync(reference, _metadata.Extract(document), fetched.ContentHash, ct);

        ValidationResult validation = _validator.Validate(document);
        if (!validation.IsValid)
        {
            await _store.RecordRejectionAsync(reference, string.Join("; ", validation.Problems), ct);
            return;
        }

        // Recusa permanente descoberta no envio (plataforma recusou, ou o conector não monta a requisição): mesmo
        // desfecho da rejeição na validação — registrada, sem retentativa (ADR-0026). Transitório propaga.
        IntegrationReceipt receipt;
        try
        {
            // O despacho é dois passos (ADR-0041): preparar traduz e fotografa o payload de destino, sem enviar;
            // enviar faz a requisição. Quem decide é a esteira, e não o adapter.
            PreparedDispatch<TDocument> prepared = await _dispatcher.PrepareAsync(document, context, ct);

            // A chave é lida DEPOIS de preparar, de propósito: é isso que faz "a chave desligada não esconde rejeição"
            // cair de graça. A recusa de contrato do destino e a de configuração que a preparação descobre continuam
            // saindo com a chave em qualquer posição; só a nota que chegaria à requisição de envio vira "Salva".
            if (!await _dispatch.IsOnAsync(context.TenantId, ct))
            {
                await _store.RecordSavedAsync(reference, OutboundDispatchOffReason, ct);
                return;
            }

            receipt = await _dispatcher.SendAsync(prepared, context, ct);
        }
        catch (DispatchRejectedException rejected)
        {
            await _store.RecordRejectionAsync(reference, rejected.Reason, ct);
            return;
        }

        await _store.RecordSubmissionAsync(reference, receipt, ct);
    }
}
