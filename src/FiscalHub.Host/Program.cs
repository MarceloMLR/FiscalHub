using Azure.Storage.Blobs;
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
builder.Services.AddXmlGoodsInvoiceSource();
builder.Services.AddSqlProcessingStore(cfg.GetConnectionString("Sql")!);
builder.Services.AddAvalaraComplianceDispatcher(options => options.BaseUrl = cfg["Avalara:BaseUrl"]!);
builder.Services.AddSingleton<IDocumentValidator<GoodsInvoice>, GoodsInvoiceValidator>();
builder.Services.AddScoped<DocumentPipeline<GoodsInvoice>>();

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
        CorrelationId = Guid.NewGuid().ToString(),
        Operation = DocumentStatus.Issued,
    };

    await pipeline.ProcessAsync(reference, context, ct);
    return Results.Ok(new { ingested = req.NaturalKey });
});

app.Run();

/// <summary>Corpo do POST /ingest.</summary>
public sealed record IngestRequest(string TenantId, string NaturalKey, string Locator);
