using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>
/// Lê o conteúdo de texto de um blob pelo localizador. Costura de testabilidade: isola o SDK do
/// Azure (difícil de fakear) atrás de uma interface própria, deixando o source testável sem Azure.
/// </summary>
internal interface IBlobReader
{
    Task<string> ReadTextAsync(string locator, CancellationToken ct = default);
}

/// <summary>Leitor real de Blob (Azure.Storage.Blobs). Localizador no formato "container/blob".</summary>
internal sealed class AzureBlobReader : IBlobReader
{
    private readonly BlobServiceClient _client;

    public AzureBlobReader(BlobServiceClient client) => _client = client;

    public async Task<string> ReadTextAsync(string locator, CancellationToken ct = default)
    {
        int slash = locator.IndexOf('/');
        if (slash <= 0 || slash == locator.Length - 1)
        {
            throw new ArgumentException($"Localizador inválido: '{locator}'. Esperado 'container/blob'.", nameof(locator));
        }

        string container = locator[..slash];
        string blobName = locator[(slash + 1)..];

        BlobClient blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
        Response<BlobDownloadResult> response = await blob.DownloadContentAsync(ct);
        return response.Value.Content.ToString();
    }
}
