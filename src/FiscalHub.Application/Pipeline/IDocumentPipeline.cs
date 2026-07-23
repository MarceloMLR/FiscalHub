using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Núcleo da esteira: processa um documento de ponta a ponta. Interface para desacoplar os
/// disparadores (endpoint HTTP, gatilho de fila) da implementação concreta — e para testá-los.
/// </summary>
public interface IDocumentPipeline<TDocument>
{
    /// <summary>Processa um documento: idempotência, busca, validação, envio e registro.</summary>
    Task ProcessAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default);
}
