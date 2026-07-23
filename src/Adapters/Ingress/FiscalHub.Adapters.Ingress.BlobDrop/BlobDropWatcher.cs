using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Ingress.BlobDrop;

/// <summary>
/// Gatilho de ingestão para dev local: observa uma zona de drop no Blob e, para cada arquivo novo,
/// move pro container durável e enfileira a referência (claim-check). No cloud, este adapter é
/// trocado por Event Grid (o arquivo no Blob dispara um evento) — a arquitetura é a mesma, muda só
/// a origem do gatilho. Aqui a gente varre em intervalo porque o Azurite não emite eventos.
/// </summary>
internal sealed class BlobDropWatcher : BackgroundService
{
    private readonly BlobServiceClient _blobs;
    private readonly IDocumentQueue _queue;
    private readonly ILogger<BlobDropWatcher> _logger;
    private readonly BlobDropOptions _options;

    public BlobDropWatcher(
        BlobServiceClient blobs,
        IDocumentQueue queue,
        IOptions<BlobDropOptions> options,
        ILogger<BlobDropWatcher> logger)
    {
        _blobs = blobs;
        _queue = queue;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BlobContainerClient drop = _blobs.GetBlobContainerClient(_options.DropContainer);
        BlobContainerClient inbox = _blobs.GetBlobContainerClient(_options.InboxContainer);
        await drop.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
        await inbox.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        using var timer = new PeriodicTimer(_options.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(drop, inbox, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao varrer a zona de drop.");
            }
        }
    }

    private async Task SweepAsync(BlobContainerClient drop, BlobContainerClient inbox, CancellationToken ct)
    {
        await foreach (BlobItem item in drop.GetBlobsAsync(cancellationToken: ct))
        {
            (string tenant, string key) = DropBlobNaming.Parse(item.Name, _options.DefaultTenant);
            string inboxName = $"{tenant}/{key}.xml";

            // Move: baixa da zona de drop, grava no container durável, apaga o original. O locator
            // enfileirado aponta pro container durável, então o consumidor lê de um lugar estável.
            BlobClient source = drop.GetBlobClient(item.Name);
            BlobDownloadResult content = await source.DownloadContentAsync(ct);
            await inbox.GetBlobClient(inboxName).UploadAsync(content.Content.ToStream(), overwrite: true, ct);

            var reference = new DocumentReference
            {
                TenantId = tenant,
                Type = DocumentType.GoodsInvoice55,
                NaturalKey = key,
                Locator = $"{_options.InboxContainer}/{inboxName}",
            };

            await _queue.EnqueueAsync(reference, ct);
            await source.DeleteIfExistsAsync(cancellationToken: ct);

            _logger.LogInformation("Drop ingerido: {Blob} → fila (tenant {Tenant}, chave {Key}).", item.Name, tenant, key);
        }
    }
}
