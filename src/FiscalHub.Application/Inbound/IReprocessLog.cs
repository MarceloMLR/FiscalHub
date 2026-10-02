namespace FiscalHub.Application.Inbound;

/// <summary>
/// A contagem de reprocessos do registro do documento, que o modal do grupo mostra ao lado das consultas de status
/// (conferência na tela da change erp-company-directory-and-card-filters, 2026-10-02). Ela soma um a cada reprocesso aceito,
/// e não zera no reenvio, ao contrário das consultas. Uma porta própria, e não um método do <c>IProcessingStore</c>, para não
/// engordar a porta de escrita da esteira nem os fakes dela.
/// </summary>
public interface IReprocessLog
{
    /// <summary>Soma um na contagem da nota do tenant. A nota sem registro não tem o que contar, e não falha.</summary>
    Task RecordAsync(string tenantId, string naturalKey, CancellationToken ct = default);
}
