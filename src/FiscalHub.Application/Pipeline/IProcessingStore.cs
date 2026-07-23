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

    /// <summary>Registra que o documento foi rejeitado na validação de integração, com o motivo.</summary>
    Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default);

    /// <summary>Lista documentos em voo (enviados, aguardando confirmação) para consulta de status.</summary>
    Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Atualiza o desfecho de uma consulta de status: novo estado, motivo e nº de tentativas.</summary>
    Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default);
}

/// <summary>Documento em voo a consultar: identidade, GUID externo e tentativas já feitas.</summary>
public sealed record PendingIntegration
{
    public required string TenantId { get; init; }
    public required string NaturalKey { get; init; }
    public required string ExternalId { get; init; }
    public required int Attempts { get; init; }
}
