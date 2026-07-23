namespace FiscalHub.Application.Inbound;

/// <summary>
/// Enfileira a referência de um documento para processamento assíncrono (claim-check): o gatilho
/// publica só o ponteiro leve; a esteira busca o documento pesado depois. Implementada pelo adapter
/// de mensageria (Service Bus).
/// </summary>
public interface IDocumentQueue
{
    /// <summary>Publica a referência na fila de entrada.</summary>
    Task EnqueueAsync(DocumentReference reference, CancellationToken ct = default);
}
