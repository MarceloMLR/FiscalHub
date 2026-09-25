using FiscalHub.Application.Coordination;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Persiste o cursor do feed de mudanças por (tenant, origem). Implementada na Infrastructure. O avanço
/// da marca é condicionado à posse do lease na mesma operação atômica (fencing, ADR-0024): uma réplica
/// que perdeu o lease não consegue gravar.
/// </summary>
public interface IChangeFeedCursorStore
{
    /// <summary>Lê o cursor; <c>null</c> se o par nunca foi consultado nem teve falha registrada.</summary>
    Task<ChangeFeedCursor?> GetAsync(string tenantId, string origin, CancellationToken ct = default);

    /// <summary>
    /// Garante que o cursor tem marca: cria com <paramref name="initialWatermark"/> se não existe, ou
    /// preenche se existe sem marca. Se já tem marca, não mexe. Devolve o cursor como ficou.
    /// </summary>
    Task<ChangeFeedCursor> StartAsync(string tenantId, string origin, DateTimeOffset initialWatermark, CancellationToken ct = default);

    /// <summary>
    /// Avança a marca para <paramref name="watermark"/> se ela for maior que a atual E se o lease ainda
    /// for de quem pede, tudo numa operação só. <c>false</c> = nada gravado (lease perdido ou marca não
    /// maior): o worker encerra o poll do tenant.
    /// </summary>
    Task<bool> TryAdvanceWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default);

    /// <summary>Registra um poll bem-sucedido: último poll = agora, falhas zeradas, erro e throttling limpos.</summary>
    Task RecordSuccessAsync(string tenantId, string origin, DateTimeOffset polledAt, CancellationToken ct = default);

    /// <summary>Registra uma falha (cria o cursor, sem marca, se preciso): incrementa as falhas e guarda o erro e o throttling.</summary>
    Task RecordFailureAsync(string tenantId, string origin, DateTimeOffset polledAt, string error, DateTimeOffset? notBefore, CancellationToken ct = default);
}
