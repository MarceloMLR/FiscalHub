using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Porta de entrada dos gatilhos de fila: decide o que fazer com uma referência — levar à esteira do tipo dela
/// ou registrar o desfecho "ignorado" (ADR-0025). Interface para testar os consumidores sem a esteira real.
/// </summary>
public interface IDocumentRouter
{
    /// <summary>Roteia a referência. Exceção = falha de processamento, a cargo do retry/DLQ do transporte.</summary>
    Task RouteAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default);
}
