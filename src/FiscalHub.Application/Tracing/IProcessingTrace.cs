namespace FiscalHub.Application.Tracing;

/// <summary>
/// Guarda as "fotos" de um documento ao longo do processamento, para rastreabilidade em chamados:
/// o layout do domínio (nosso padrão) e o payload enviado ao destino. A fonte crua (XML) já fica
/// no Blob pelo claim-check. Com as três, isola-se onde uma informação se perdeu:
/// fonte → domínio → destino.
/// </summary>
public interface IProcessingTrace
{
    /// <summary>Registra o documento já no nosso modelo de domínio (JSON).</summary>
    Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default);

    /// <summary>Registra o payload enviado a um destino (ex.: o god json da Avalara).</summary>
    Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default);
}

/// <summary>Trace desligado (no-op). Padrão quando a rastreabilidade não está configurada.</summary>
public sealed class NoOpProcessingTrace : IProcessingTrace
{
    public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        => Task.CompletedTask;
}
