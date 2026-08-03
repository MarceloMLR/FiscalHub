namespace FiscalHub.Application.Support;

/// <summary>
/// Orquestra a abertura de um chamado a partir de uma ou mais notas: monta os anexos (um zip por
/// nota), compõe a descrição com as infos de integração e chama o provider do tenant.
/// </summary>
public interface ISupportTicketService
{
    Task<TicketResult> OpenAsync(
        string tenantId,
        IReadOnlyList<string> naturalKeys,
        string subject,
        string description,
        IReadOnlyList<TicketAttachment>? extraAttachments = null,
        CancellationToken ct = default);

    /// <summary>Tamanho (bytes) dos anexos automáticos — os zips de logs das notas — pra tela mostrar o disponível.</summary>
    Task<long> EstimateLogsBytesAsync(string tenantId, IReadOnlyList<string> naturalKeys, CancellationToken ct = default);
}
