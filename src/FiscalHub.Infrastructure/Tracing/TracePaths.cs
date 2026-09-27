using FiscalHub.Application.Tracing;

namespace FiscalHub.Infrastructure.Tracing;

/// <summary>
/// A regra do layout das fotos no Blob, num lugar só: <c>{tenant}/{aaaaMM}/{chave}/{arquivo}</c>. Todas as fotos de um
/// documento ficam sob o mesmo prefixo, e por isso o <c>/trace</c> e o zip as incluem sem regra própria. Cada foto tem
/// um nome distinto no zip:
/// <list type="bullet">
///   <item><c>source.{formato}</c> — a fonte crua;</item>
///   <item><c>domain.json</c> — o domínio;</item>
///   <item><c>{destino}.json</c> — o payload de destino;</item>
///   <item><c>{destino}.response.submit.json</c> e <c>{destino}.response.status.json</c> — as respostas (ADR-0027).</item>
/// </list>
/// </summary>
internal static class TracePaths
{
    public const string Domain = "domain.json";

    public static string Source(string format) => $"source.{format}";

    public static string Outbound(string destination) => $"{destination}.json";

    public static string Response(string destination, string exchange) => exchange switch
    {
        TraceExchanges.Submit or TraceExchanges.Status => $"{destination}.response.{exchange}.json",
        _ => throw new ArgumentException($"Troca sem foto de resposta: '{exchange}'.", nameof(exchange)),
    };

    /// <summary>O prefixo de todas as fotos de um documento no período.</summary>
    public static string DocumentPrefix(string tenantId, string period, string naturalKey) => $"{tenantId}/{period}/{naturalKey}/";

    public static string Blob(string tenantId, string period, string naturalKey, string fileName)
        => DocumentPrefix(tenantId, period, naturalKey) + fileName;
}
