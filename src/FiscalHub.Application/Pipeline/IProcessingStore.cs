using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Pipeline;

/// <summary>
/// Persiste o rastreio de processamento dos documentos e responde à pergunta de idempotência.
/// Implementada na Infrastructure (Azure SQL); o núcleo só conhece esta porta.
/// </summary>
public interface IProcessingStore
{
    /// <summary>
    /// Indica se o documento já foi enviado com sucesso ao destino. Usada para evitar reenvio
    /// quando a mesma mensagem é reentregue (retry ou entrega duplicada do Service Bus).
    /// </summary>
    Task<bool> AlreadySubmittedAsync(string tenantId, string naturalKey, CancellationToken ct = default);

    /// <summary>Registra o resultado do envio: status de integração e identificador externo.</summary>
    Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default);
}
