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

    private readonly IInboundSource<TDocument> _source;
    private readonly IDocumentValidator<TDocument> _validator;
    private readonly IComplianceDispatcher<TDocument> _dispatcher;
    private readonly IProcessingStore _store;
    private readonly IProcessingTrace _trace;
    private readonly IDocumentMetadataExtractor<TDocument> _metadata;

    public DocumentPipeline(
        IInboundSource<TDocument> source,
        IDocumentValidator<TDocument> validator,
        IComplianceDispatcher<TDocument> dispatcher,
        IProcessingStore store,
        IProcessingTrace trace,
        IDocumentMetadataExtractor<TDocument> metadata)
    {
        _source = source;
        _validator = validator;
        _dispatcher = dispatcher;
        _store = store;
        _trace = trace;
        _metadata = metadata;
    }

    /// <summary>
    /// Processa um documento: idempotência, busca, validação de integração, envio e registro.
    /// Documento inválido é rejeitado (registrado com o motivo) e não é enviado. Qualquer falha
    /// propaga como exceção, deixando o retry/DLQ a cargo do transporte.
    /// </summary>
    public async Task ProcessAsync(
        DocumentReference reference, DispatchContext context, CancellationToken ct = default)
    {
        if (await _store.AlreadySubmittedAsync(reference.TenantId, reference.NaturalKey, ct))
            return;

        TDocument document = await _source.FetchAsync(reference, ct);

        // Foto do domínio (ADR-0006): o documento já no nosso modelo, antes de validar — assim até
        // uma nota rejeitada deixa registrado o que a gente entendeu dela.
        await _trace.SaveDomainAsync(reference.TenantId, reference.NaturalKey, JsonSerializer.Serialize(document, JsonOpts), ct);

        // Metadados de agrupamento (empresa/filial/data) na primeira passada — assim até uma nota
        // rejeitada aparece no grupo certo do dashboard.
        await _store.RecordMetadataAsync(reference, _metadata.Extract(document), ct);

        ValidationResult validation = _validator.Validate(document);
        if (!validation.IsValid)
        {
            await _store.RecordRejectionAsync(reference, string.Join("; ", validation.Problems), ct);
            return;
        }

        IntegrationReceipt receipt = await _dispatcher.SubmitAsync(document, context, ct);

        await _store.RecordSubmissionAsync(reference, receipt, ct);
    }
}
