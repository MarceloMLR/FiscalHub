using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Application.Tracing;

namespace FiscalHub.Infrastructure.Tracing;

/// <summary>
/// Grava as fotos de rastreabilidade no Blob (object storage — feito para escala; milhões de
/// objetos são o uso normal, e a retenção sai por lifecycle policy no container, fora do código).
/// Layout: <c>{tenant}/{aaaaMM}/{chave}/domain.json</c> e <c>.../{destino}.json</c>. Reprocessar
/// o mesmo documento sobrescreve a foto.
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

    public Task SaveDomainAsync(string tenantId, string naturalKey, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, "domain", json, ct);

    public Task SaveOutboundAsync(string tenantId, string naturalKey, string destination, string json, CancellationToken ct = default)
        => WriteAsync(tenantId, naturalKey, destination, json, ct);

    private async Task WriteAsync(string tenantId, string naturalKey, string name, string json, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        string period = _time.GetUtcNow().ToString("yyyyMM");
        BlobClient blob = _container.GetBlobClient($"{tenantId}/{period}/{naturalKey}/{name}.json");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } },
            ct);
    }
}
