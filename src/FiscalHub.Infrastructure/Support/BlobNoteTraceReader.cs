using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Application.Support;

namespace FiscalHub.Infrastructure.Support;

/// <summary>
/// Lê os arquivos de rastreabilidade de uma nota do container "traces" (origem/domínio/destino),
/// crus, pra anexar ao chamado. Mesma varredura do endpoint de download, só que devolve bytes.
/// </summary>
internal sealed class BlobNoteTraceReader : INoteTraceReader
{
    private const string Container = "traces";
    private readonly BlobServiceClient _blobs;

    public BlobNoteTraceReader(BlobServiceClient blobs) => _blobs = blobs;

    public async Task<IReadOnlyList<TraceFile>> ReadAsync(string tenantId, string naturalKey, CancellationToken ct = default)
    {
        BlobContainerClient container = _blobs.GetBlobContainerClient(Container);
        if (!(await container.ExistsAsync(ct)).Value)
        {
            return [];
        }

        var files = new List<TraceFile>();
        await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{tenantId}/", ct))
        {
            if (!item.Name.Contains($"/{naturalKey}/", StringComparison.Ordinal))
            {
                continue;
            }

            BlobDownloadResult blob = await container.GetBlobClient(item.Name).DownloadContentAsync(ct);
            files.Add(new TraceFile(item.Name.Split('/')[^1], blob.Content.ToArray()));
        }

        return files;
    }
}
