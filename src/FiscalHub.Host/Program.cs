using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Adapters.Inbound.Xml;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using FiscalHub.Host;
using FiscalHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Composição: Blob (Azurite) + adapters + store + validação + esteira.
builder.Services.AddSingleton(new BlobServiceClient(cfg.GetConnectionString("Blob")));
builder.Services.AddBlobProcessingTrace("traces");
builder.Services.AddXmlGoodsInvoiceSource();
builder.Services.AddSqlProcessingStore(cfg.GetConnectionString("Sql")!);
builder.Services.AddAvalaraComplianceDispatcher(options => options.BaseUrl = cfg["Avalara:BaseUrl"]!);
builder.Services.AddSingleton<IDocumentValidator<GoodsInvoice>, GoodsInvoiceValidator>();
builder.Services.AddScoped<DocumentPipeline<GoodsInvoice>>();

// Poll de status: consulta os documentos em voo e fecha o ciclo (confirma/erro/unconfirmed).
builder.Services.AddSingleton(new StatusPollerOptions());
builder.Services.AddScoped<StatusPoller<GoodsInvoice>>();
builder.Services.AddHostedService<StatusPollingService>();

var app = builder.Build();

// Dev local: cria o schema no SQL, o container no Blob e sobe um XML de exemplo.
await app.Services.EnsureProcessingSchemaAsync();
await LocalSeed.RunAsync(app.Services);

app.MapGet("/", () =>
    $"FiscalHub host. POST /ingest com {{ tenantId, naturalKey, locator }}. XML de exemplo semeado em '{LocalSeed.Locator}'.");

// Etapa 1 (sem fila): dispara a esteira para um documento. A fila entra na Etapa 2.
app.MapPost("/ingest", async (IngestRequest req, DocumentPipeline<GoodsInvoice> pipeline, CancellationToken ct) =>
{
    var reference = new DocumentReference
    {
        TenantId = req.TenantId,
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = req.NaturalKey,
        Locator = req.Locator,
    };
    var context = new DispatchContext
    {
        TenantId = req.TenantId,
        NaturalKey = req.NaturalKey,
        CorrelationId = Guid.NewGuid().ToString(),
        Operation = DocumentStatus.Issued,
    };

    await pipeline.ProcessAsync(reference, context, ct);
    return Results.Ok(new { ingested = req.NaturalKey });
});

// Debug (dev local): devolve as fotos de rastreabilidade de um documento — dominio e destino,
// direto do Blob, sem Storage Explorer. A fonte crua (XML) fica no container de entrada.
app.MapGet("/trace/{tenantId}/{naturalKey}", async (string tenantId, string naturalKey, BlobServiceClient blobs, CancellationToken ct) =>
{
    BlobContainerClient container = blobs.GetBlobContainerClient("traces");
    if (!(await container.ExistsAsync(ct)).Value)
    {
        return Results.NotFound(new { message = "container 'traces' ainda nao existe — rode um /ingest antes." });
    }

    var snapshots = new Dictionary<string, object>();
    await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{tenantId}/", ct))
    {
        if (!item.Name.Contains($"/{naturalKey}/", StringComparison.Ordinal))
        {
            continue;
        }

        BlobDownloadResult blob = await container.GetBlobClient(item.Name).DownloadContentAsync(ct);
        // JSON entra aninhado (legível); a fonte crua (XML) entra como string.
        snapshots[item.Name] = item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? blob.Content.ToObjectFromJson<JsonElement>()
            : blob.Content.ToString();
    }

    return snapshots.Count == 0
        ? Results.NotFound(new { tenantId, naturalKey, message = "sem fotos para esse documento." })
        : Results.Ok(snapshots);
});

app.Run();

/// <summary>Corpo do POST /ingest.</summary>
public sealed record IngestRequest(string TenantId, string NaturalKey, string Locator);
