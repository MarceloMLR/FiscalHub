using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Application.Tracing;

namespace FiscalHub.Infrastructure.Tracing;

/// <summary>
/// Grava as fotos de rastreabilidade no Blob (object storage — feito para escala; milhões de
/// objetos são o uso normal, e a retenção sai por lifecycle policy no container, fora do código).
/// O layout é o do <see cref="TracePaths"/>: a fonte, o domínio, o payload de destino e as duas respostas, sob o prefixo
/// do documento. Reprocessar o mesmo documento sobrescreve a foto.
/// </summary>
internal sealed class BlobProcessingTrace : IProcessingTrace
{
    private readonly BlobContainerClient _container;
    private readonly TimeProvider _time;

    public BlobProcessingTrace(BlobServiceClient client, string containerName, TimeProvider time)
    {
        _container = client.GetBlobContainerClient(containerName);
        _time = time;
    }

    public Task SaveSourceAsync(string tenantId, string naturalKey, string content, string format, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, TracePaths.Source(format), MediaTypeFor(format), content, ct);

    public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, TracePaths.Domain, "application/json", json, ct);

    public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, TracePaths.Outbound(destination), "application/json", json, ct);

    public Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, TracePaths.Response(destination, exchange), "application/json", json, ct);

    private async Task WriteAsync(string tenantId, string naturalKey, string fileName, string contentType, string content, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        string period = _time.GetUtcNow().ToString("yyyyMM");
        BlobClient blob = _container.GetBlobClient(TracePaths.Blob(tenantId, period, naturalKey, fileName));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
    }

    private static string MediaTypeFor(string format) => format.ToLowerInvariant() switch
    {
        "xml" => "application/xml",
        "json" => "application/json",
        _ => "text/plain",
    };
}
