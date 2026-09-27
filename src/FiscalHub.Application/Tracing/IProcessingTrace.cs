namespace FiscalHub.Application.Tracing;

/// <summary>
/// Guarda as "fotos" de um documento para rastreabilidade em chamados: a fonte crua recebida do cliente, o documento
/// no nosso modelo de domínio, o payload enviado ao destino e o que o destino respondeu (ADR-0027). Com elas, isola-se
/// onde uma informação se perdeu: fonte → domínio → destino → resposta. Cada camada fotografa o artefato que é dona
/// (entrada → fonte, esteira → domínio, saída → destino e resposta).
/// </summary>
public interface IProcessingTrace
{
    /// <summary>Registra a fonte crua recebida do cliente (ex.: o XML do ERP), no formato original.</summary>
    Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default);

    /// <summary>Registra o documento já no nosso modelo de domínio (JSON).</summary>
    Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default);

    /// <summary>Registra o payload enviado a um destino (ex.: o god json da Avalara).</summary>
    Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default);

    /// <summary>
    /// Registra a resposta do destino a uma troca (<see cref="TraceExchanges"/>): o envelope já redigido, com status,
    /// URL sem query, os cabeçalhos permitidos e o corpo. A última troca de cada tipo sobrescreve a anterior.
    /// </summary>
    Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default);
}

/// <summary>As trocas com o destino que têm foto da resposta. A do endpoint de token nunca é fotografada.</summary>
public static class TraceExchanges
{
    /// <summary>A resposta da última tentativa de envio.</summary>
    public const string Submit = "submit";

    /// <summary>A última consulta de status com corpo.</summary>
    public const string Status = "status";
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

    public Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default)
        => Task.CompletedTask;
}
