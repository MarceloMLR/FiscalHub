namespace FiscalHub.Application.Support;

/// <summary>
/// Lê os arquivos de rastreabilidade de uma nota (origem/domínio/destino), crus, pra anexar ao
/// chamado. Implementado na Infrastructure (Blob), igual ao que o endpoint de download já faz.
/// </summary>
public interface INoteTraceReader
{
    Task<IReadOnlyList<TraceFile>> ReadAsync(string tenantId, string naturalKey, CancellationToken ct = default);
}
