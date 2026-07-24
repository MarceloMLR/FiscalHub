using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Adapters.Directory.Json;
using FiscalHub.Adapters.Discovery.Local;
using FiscalHub.Adapters.Inbound.Xml;
using FiscalHub.Adapters.Ingress.BlobDrop;
using FiscalHub.Adapters.Messaging.ServiceBus;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Application.Directory;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Integrations;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Queries;
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
builder.Services.AddSingleton<IDocumentMetadataExtractor<GoodsInvoice>, GoodsInvoiceMetadataExtractor>();
builder.Services.AddScoped<IDocumentPipeline<GoodsInvoice>, DocumentPipeline<GoodsInvoice>>();

// Gatilho por fila (Etapa 2): /ingest enfileira; o consumidor do Service Bus chama a esteira,
// com retry e dead-letter nativos do transporte.
builder.Services.AddServiceBusDocumentQueue(options =>
{
    options.ConnectionString = cfg.GetConnectionString("ServiceBus")!;
    options.QueueName = cfg["ServiceBus:Queue"] ?? "documents-in";
});

// Gatilho de ingestão (dev local): observa a zona de drop no Blob e enfileira sozinho.
// No cloud, este watcher é trocado por Event Grid.
builder.Services.AddBlobDropIngress();

// Diretório de empresas/filiais (dev local, via JSON) — alimenta os dropdowns da integração manual.
builder.Services.AddJsonCompanyDirectory(o =>
    o.FilePath = Path.Combine(builder.Environment.ContentRootPath, "companies.json"));

// Descoberta pull (dev local): busca as notas de um período na "origem". No cloud, vira um adapter
// que consulta a Avalara/ERP — a porta e o endpoint de integração manual não mudam.
builder.Services.AddLocalDocumentDiscovery();

// Runner de integração: descobre → enfileira → registra a execução. Compartilhado pela integração
// manual e pelo agendador.
builder.Services.AddScoped<IIntegrationRunner, IntegrationRunner>();

// Poll de status: consulta os documentos em voo e fecha o ciclo (confirma/erro/unconfirmed).
builder.Services.AddSingleton(new StatusPollerOptions());
builder.Services.AddScoped<StatusPoller<GoodsInvoice>>();
builder.Services.AddHostedService<StatusPollingService>();

// CORS liberado pro dashboard local. Em produção, restringir a origem.
builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Enums como texto no JSON das respostas (status/tipo legíveis pro dashboard, não números).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseCors();

// Dev local: cria o schema no SQL, o container no Blob e sobe um XML de exemplo.
await app.Services.MigrateProcessingSchemaAsync();
await LocalSeed.RunAsync(app.Services);

app.MapGet("/", () =>
    $"FiscalHub host. POST /ingest com {{ tenantId, naturalKey, locator }}. XML de exemplo semeado em '{LocalSeed.Locator}'.");

// Etapa 2: enfileira a referência (claim-check). O consumidor do Service Bus processa a esteira;
// retry e dead-letter ficam por conta do transporte.
app.MapPost("/ingest", async (IngestRequest req, IDocumentQueue queue, CancellationToken ct) =>
{
    var reference = new DocumentReference
    {
        TenantId = req.TenantId,
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = req.NaturalKey,
        Locator = req.Locator,
    };

    await queue.EnqueueAsync(reference, ct);
    return Results.Accepted($"/trace/{req.TenantId}/{req.NaturalKey}", new { queued = req.NaturalKey });
});

// Integração manual (modo pull): o cliente escolhe empresa/filial/período; a descoberta lista as
// notas daquele recorte na origem e o conector enfileira cada referência no padrão claim-check.
// Reprocessar o mesmo período é idempotente — a mesma chave de acesso cai na regra por estado.
app.MapPost("/integrations/manual", async (ManualIntegrationRequest req, IIntegrationRunner runner, CancellationToken ct) =>
{
    int discovered = await runner.RunAsync(new RunRequest
    {
        Mode = IntegrationMode.Manual,
        TenantId = req.TenantId ?? "tenant-a",
        CompanyCode = req.CompanyCode,
        BranchCode = string.IsNullOrWhiteSpace(req.BranchCode) ? null : req.BranchCode,
        PeriodStart = req.PeriodStart,
        PeriodEnd = req.PeriodEnd,
    }, ct);

    return Results.Accepted("/documents", new { discovered });
});

// Execuções recentes (manuais/agendadas) pro painel: modo, empresa/filial, período e nº de notas.
app.MapGet("/executions", async (IExecutionQueries queries, CancellationToken ct) =>
    Results.Ok(await queries.ListRecentAsync(100, ct)));

// Debug (dev local): copia o XML de exemplo pra zona de drop, simulando um arquivo que "cai" no
// Blob. O watcher de ingestão pega, move pro container durável e enfileira — sem /ingest manual.
app.MapPost("/drop/{key}", async (string key, string? empresa, BlobServiceClient blobs, CancellationToken ct) =>
{
    string sampleName = string.Equals(empresa, "b", StringComparison.OrdinalIgnoreCase) ? LocalSeed.BlobName2 : LocalSeed.BlobName;
    BlobClient sample = blobs.GetBlobContainerClient(LocalSeed.Container).GetBlobClient(sampleName);
    if (!(await sample.ExistsAsync(ct)).Value)
    {
        return Results.NotFound(new { message = "XML de exemplo ainda não semeado." });
    }

    BlobDownloadResult content = await sample.DownloadContentAsync(ct);
    BlobContainerClient drop = blobs.GetBlobContainerClient("drop");
    await drop.CreateIfNotExistsAsync(cancellationToken: ct);
    await drop.GetBlobClient($"tenant-a/{key}.xml").UploadAsync(content.Content.ToStream(), overwrite: true, ct);

    return Results.Accepted($"/trace/tenant-a/{key}", new { dropped = $"tenant-a/{key}.xml" });
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

// Download: zipa as fotos (fonte/domínio/destino) de um documento pra baixar de uma vez.
app.MapGet("/documents/{tenantId}/{naturalKey}/download", async (string tenantId, string naturalKey, BlobServiceClient blobs, CancellationToken ct) =>
{
    BlobContainerClient container = blobs.GetBlobContainerClient("traces");
    if (!(await container.ExistsAsync(ct)).Value)
    {
        return Results.NotFound(new { message = "sem arquivos para esse documento." });
    }

    var zipStream = new MemoryStream();
    var added = 0;
    using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{tenantId}/", ct))
        {
            if (!item.Name.Contains($"/{naturalKey}/", StringComparison.Ordinal))
            {
                continue;
            }

            BlobDownloadResult blob = await container.GetBlobClient(item.Name).DownloadContentAsync(ct);
            ZipArchiveEntry entry = zip.CreateEntry(item.Name.Split('/')[^1], CompressionLevel.Optimal);
            await using Stream entryStream = entry.Open();
            using Stream source = blob.Content.ToStream();
            await source.CopyToAsync(entryStream, ct);
            added++;
        }
    }

    if (added == 0)
    {
        return Results.NotFound(new { message = "sem arquivos para esse documento." });
    }

    return Results.File(zipStream.ToArray(), "application/zip", $"{naturalKey}.zip");
});

// Leitura pro dashboard: os documentos mais recentes com status. Em produção, atrás de auth e
// filtrado por tenant.
app.MapGet("/documents", async (IDocumentQueries queries, CancellationToken ct) =>
    Results.Ok(await queries.ListRecentAsync(100, ct)));

// Dashboard: grupos (empresa/filial/dia) com contagens, e os documentos de um grupo.
app.MapGet("/groups", async (IDocumentQueries queries, CancellationToken ct) =>
    Results.Ok(await queries.ListGroupsAsync(200, ct)));

app.MapGet("/groups/{companyCode}/{branchCode}/{referenceDate}/documents",
    async (string companyCode, string branchCode, string referenceDate, IDocumentQueries queries, CancellationToken ct) =>
        Results.Ok(await queries.ListByGroupAsync(companyCode, branchCode, referenceDate, ct)));

// Diretório de empresas e filiais (dropdowns da integração manual).
app.MapGet("/companies", async (ICompanyDirectory dir, CancellationToken ct) =>
    Results.Ok(await dir.ListCompaniesAsync(ct)));

app.MapGet("/companies/{code}/branches", async (string code, ICompanyDirectory dir, CancellationToken ct) =>
    Results.Ok(await dir.ListBranchesAsync(code, ct)));

// Ambiente do conector (sandbox/produção) — o dashboard exibe no topo.
app.MapGet("/info", () => Results.Ok(new { environment = cfg["Connector:Environment"] ?? "Sandbox" }));

app.Run();

/// <summary>Corpo do POST /ingest.</summary>
public sealed record IngestRequest(string TenantId, string NaturalKey, string Locator);

/// <summary>Corpo do POST /integrations/manual. Filial vazia = todas; tenant nulo cai no de dev.</summary>
public sealed record ManualIntegrationRequest(
    string CompanyCode,
    string? BranchCode,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string? TenantId);
