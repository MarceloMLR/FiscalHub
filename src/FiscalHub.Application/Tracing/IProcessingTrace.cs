namespace FiscalHub.Application.Tracing;

/// <summary>
/// Guarda as três "fotos" de um documento para rastreabilidade em chamados: a fonte crua recebida
/// do cliente, o documento no nosso modelo de domínio, e o payload enviado ao destino. Com as três,
/// isola-se onde uma informação se perdeu: fonte → domínio → destino. Cada camada fotografa o
/// artefato que é dona (entrada → fonte, esteira → domínio, saída → destino).
/// </summary>
public interface IProcessingTrace
{
    /// <summary>Registra a fonte crua recebida do cliente (ex.: o XML do ERP), no formato original.</summary>
    Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default);

    /// <summary>Registra o documento já no nosso modelo de domínio (JSON).</summary>
    Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default);

    /// <summary>Registra o payload enviado a um destino (ex.: o god json da Avalara).</summary>
    Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default);
}

/// <summary>Trace desligado (no-op). Padrão quando a rastreabilidade não está configurada.</summary>
public sealed class NoOpProcessingTrace : IProcessingTrace
{
    public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        => Task.CompletedTask;
}
