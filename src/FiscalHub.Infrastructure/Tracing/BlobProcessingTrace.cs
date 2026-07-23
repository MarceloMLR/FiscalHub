using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Application.Tracing;

namespace FiscalHub.Infrastructure.Tracing;

/// <summary>
/// Grava as fotos de rastreabilidade no Blob (object storage — feito para escala; milhões de
/// objetos são o uso normal, e a retenção sai por lifecycle policy no container, fora do código).
/// Layout: <c>{tenant}/{aaaaMM}/{chave}/source.{fmt}</c>, <c>.../domain.json</c> e
/// <c>.../{destino}.json</c>. Reprocessar o mesmo documento sobrescreve a foto.
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
        => WriteAsync(tenantId, naturalKey, $"source.{format}", MediaTypeFor(format), content, ct);

    public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, "domain.json", "application/json", json, ct);

    public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, $"{destination}.json", "application/json", json, ct);

    private async Task WriteAsync(string tenantId, string naturalKey, string fileName, string contentType, string content, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        string period = _time.GetUtcNow().ToString("yyyyMM");
        BlobClient blob = _container.GetBlobClient($"{tenantId}/{period}/{naturalKey}/{fileName}");

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
