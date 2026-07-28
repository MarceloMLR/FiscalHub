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
using FiscalHub.Application.Connectors;
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
using FiscalHub.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Auth (JWT próprio): a chave/issuer/audience vêm da config. O host emite o token no /auth/login e
// valida o Bearer nas demais rotas. Em produção a chave é um segredo (Key Vault), não o appsettings.
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;   // mantém os claims com os nomes originais (tenant, role, name)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "role",
        };
    });

// Tudo exige autenticação por padrão (fallback policy); os endpoints públicos marcam AllowAnonymous.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

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

// Agendador: um timer executa os agendamentos vencidos (D-1 recorrente / único) pelo mesmo runner.
builder.Services.AddScoped<IntegrationScheduler>();
builder.Services.AddHostedService<SchedulerHostedService>();

// CORS liberado pro dashboard local. Em produção, restringir a origem.
builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Enums como texto no JSON das respostas (status/tipo legíveis pro dashboard, não números).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Dev local: cria o schema no SQL, o container no Blob, sobe um XML de exemplo e semeia os usuários.
await app.Services.MigrateProcessingSchemaAsync();
await LocalSeed.RunAsync(app.Services);
await app.Services.EnsureDevUsersAsync();
await app.Services.EnsureDevConnectorProfilesAsync();
await app.Services.EnsureDevDocumentsAsync();   // notas de exemplo p/ paginação, KPIs do dia e reprocessar

app.MapGet("/", () =>
    $"FiscalHub host. POST /ingest com {{ tenantId, naturalKey, locator }}. XML de exemplo semeado em '{LocalSeed.Locator}'.")
    .AllowAnonymous();

// Login: valida credenciais e devolve um JWT com os claims do usuário (inclui o tenant).
app.MapPost("/auth/login", async (LoginRequest req, IUserAuthenticator auth, JwtTokenIssuer issuer, CancellationToken ct) =>
{
    AppUser? user = await auth.AuthenticateAsync(req.Email, req.Password, ct);
    if (user is null)
    {
        return Results.Unauthorized();   // usuário inexistente ou senha errada — mesma resposta
    }

    (string token, DateTimeOffset expiresAt) = issuer.Issue(user);
    return Results.Ok(new
    {
        token,
        expiresAt,
        user = new { user.Email, user.Name, user.TenantId, user.Role },
    });
}).AllowAnonymous();

// Sessão atual: o SPA chama pra restaurar o login a partir do token guardado.
app.MapGet("/auth/me", (ClaimsPrincipal principal) => Results.Ok(new
{
    email = principal.FindFirstValue("email"),
    name = principal.FindFirstValue("name"),
    tenantId = principal.FindFirstValue("tenant"),
    role = principal.FindFirstValue("role"),
}));

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
app.MapPost("/integrations/manual", async (ManualIntegrationRequest req, IIntegrationRunner runner, ITenantContext tenant, CancellationToken ct) =>
{
    int discovered = await runner.RunAsync(new RunRequest
    {
        Mode = IntegrationMode.Manual,
        TenantId = tenant.TenantId,   // do usuário logado, não do body
        CompanyCode = req.CompanyCode,
        BranchCode = string.IsNullOrWhiteSpace(req.BranchCode) ? null : req.BranchCode,
        DocumentNumber = string.IsNullOrWhiteSpace(req.DocumentNumber) ? null : req.DocumentNumber,
        PeriodStart = req.PeriodStart,
        PeriodEnd = req.PeriodEnd,
    }, ct);

    return Results.Accepted("/documents", new { discovered });
});

// Execuções recentes (manuais/agendadas) pro painel: modo, empresa/filial, período e nº de notas.
app.MapGet("/executions", async (IExecutionQueries queries, CancellationToken ct) =>
    Results.Ok(await queries.ListRecentAsync(100, ct)));

// Agendamentos: cria (D-1 recorrente ou único), lista e desativa. O timer do host executa os vencidos.
// Valida o corpo e calcula o próximo disparo (compartilhado pelo POST e pelo PUT).
static (IResult? error, IntegrationMode mode, DateTimeOffset nextRun, string? periodStart, string? periodEnd)
    PlanSchedule(ScheduleRequest req, TimeProvider clock)
{
    if (!Enum.TryParse(req.Mode, out IntegrationMode mode) || mode == IntegrationMode.Manual)
    {
        return (Results.BadRequest(new { message = "Modo inválido. Use ScheduledDaily ou ScheduledOnce." }), default, default, null, null);
    }

    var brt = TimeSpan.FromHours(-3);
    if (mode == IntegrationMode.ScheduledDaily)
    {
        if (!TimeOnly.TryParse(req.TimeOfDay, out TimeOnly timeOfDay))
        {
            return (Results.BadRequest(new { message = "Informe timeOfDay no formato HH:mm." }), default, default, null, null);
        }

        DateTimeOffset nowBrt = clock.GetUtcNow().ToOffset(brt);
        var todayRun = new DateTimeOffset(nowBrt.Date.Add(timeOfDay.ToTimeSpan()), brt);
        DateTimeOffset next = todayRun > nowBrt ? todayRun : todayRun.AddDays(1);   // hoje se ainda vem, senão amanhã
        return (null, mode, next, null, null);
    }

    // ScheduledOnce
    if (req.RunAt is null || req.PeriodStart is null || req.PeriodEnd is null)
    {
        return (Results.BadRequest(new { message = "Agendamento único exige runAt, periodStart e periodEnd." }), default, default, null, null);
    }

    return (null, mode, req.RunAt.Value, req.PeriodStart.Value.ToString("yyyy-MM-dd"), req.PeriodEnd.Value.ToString("yyyy-MM-dd"));
}

app.MapPost("/schedules", async (ScheduleRequest req, IScheduleStore store, TimeProvider clock, ITenantContext tenant, CancellationToken ct) =>
{
    (IResult? error, IntegrationMode mode, DateTimeOffset nextRun, string? periodStart, string? periodEnd) = PlanSchedule(req, clock);
    if (error is not null)
    {
        return error;
    }

    int id = await store.CreateAsync(new ScheduledIntegration
    {
        Mode = mode,
        TenantId = tenant.TenantId,   // do usuário logado
        CompanyCode = req.CompanyCode,
        BranchCode = string.IsNullOrWhiteSpace(req.BranchCode) ? null : req.BranchCode,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        NextRunAt = nextRun,
    }, ct);

    return Results.Created($"/schedules/{id}", new { id, nextRunAt = nextRun });
});

app.MapPut("/schedules/{id:int}", async (int id, ScheduleRequest req, IScheduleStore store, TimeProvider clock, ITenantContext tenant, CancellationToken ct) =>
{
    (IResult? error, IntegrationMode mode, DateTimeOffset nextRun, string? periodStart, string? periodEnd) = PlanSchedule(req, clock);
    if (error is not null)
    {
        return error;
    }

    bool found = await store.UpdateAsync(new ScheduledIntegration
    {
        Id = id,
        Mode = mode,
        TenantId = tenant.TenantId,
        CompanyCode = req.CompanyCode,
        BranchCode = string.IsNullOrWhiteSpace(req.BranchCode) ? null : req.BranchCode,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        NextRunAt = nextRun,
    }, ct);

    return found ? Results.Ok(new { id, nextRunAt = nextRun }) : Results.NotFound();
});

app.MapGet("/schedules", async (IScheduleStore store, CancellationToken ct) =>
    Results.Ok(await store.ListAsync(ct)));

app.MapPost("/schedules/{id:int}/deactivate", async (int id, IScheduleStore store, CancellationToken ct) =>
{
    await store.DeactivateAsync(id, ct);
    return Results.NoContent();
});

// Reativa um recorrente pausado. O único (ScheduledOnce) não reativa — já cumpriu seu papel.
app.MapPost("/schedules/{id:int}/reactivate", async (int id, IScheduleStore store, TimeProvider clock, CancellationToken ct) =>
{
    IReadOnlyList<ScheduledIntegration> mine = await store.ListAsync(ct);   // já escopado ao tenant logado
    ScheduledIntegration? s = mine.FirstOrDefault(x => x.Id == id);
    if (s is null)
    {
        return Results.NotFound();
    }

    if (s.Mode != IntegrationMode.ScheduledDaily)
    {
        return Results.BadRequest(new { message = "Só agendamentos recorrentes (diários) podem ser reativados." });
    }

    // Mantém o horário salvo (no fuso de Brasília) e reprograma pro próximo disparo: hoje se ainda vem, senão amanhã.
    var brt = TimeSpan.FromHours(-3);
    DateTimeOffset lastBrt = s.NextRunAt.ToOffset(brt);
    var timeOfDay = TimeOnly.FromDateTime(lastBrt.DateTime);
    DateTimeOffset nowBrt = clock.GetUtcNow().ToOffset(brt);
    var todayRun = new DateTimeOffset(nowBrt.Date.Add(timeOfDay.ToTimeSpan()), brt);
    DateTimeOffset next = todayRun > nowBrt ? todayRun : todayRun.AddDays(1);

    bool found = await store.ReactivateAsync(id, next, ct);
    return found ? Results.Ok(new { id, nextRunAt = next }) : Results.NotFound();
});

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

// Reprocessar uma nota com falha: entrega o id ao adapter de entrada, que rebusca na origem e
// reenfileira. É intenção explícita do usuário → trigger Manual (fura a idempotência, ADR-0016).
app.MapPost("/documents/{tenantId}/{naturalKey}/reprocess",
    async (string tenantId, string naturalKey, IDocumentDiscovery discovery, IDocumentQueue queue, ITenantContext tenant, CancellationToken ct) =>
    {
        if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
        {
            return Results.NotFound();   // não confirma existência de nota de outro tenant
        }

        DocumentReference? reference = await discovery.FindByKeyAsync(tenantId, naturalKey, ct);
        if (reference is null)
        {
            return Results.NotFound(new { message = "Nota não encontrada na origem para reprocessar." });
        }

        await queue.EnqueueAsync(reference with { Trigger = IngestionTrigger.Manual }, ct);
        return Results.Accepted();
    });

// Diretório de empresas e filiais (dropdowns da integração manual).
app.MapGet("/companies", async (ICompanyDirectory dir, CancellationToken ct) =>
    Results.Ok(await dir.ListCompaniesAsync(ct)));

app.MapGet("/companies/{code}/branches", async (string code, ICompanyDirectory dir, CancellationToken ct) =>
    Results.Ok(await dir.ListBranchesAsync(code, ct)));

// Ambiente do conector — agora vem do perfil do tenant logado (cada tenant tem o seu).
app.MapGet("/info", async (IConnectorProfileStore profiles, ITenantContext tenant, CancellationToken ct) =>
{
    TenantConnectorProfile? profile = await profiles.GetAsync(tenant.TenantId, ct);
    return Results.Ok(new
    {
        environment = profile?.Environment ?? cfg["Connector:Environment"] ?? "Sandbox",
        realtime = profile?.Realtime ?? false,
    });
});

// Perfil de conector do tenant (config de adapters/ambiente/settings). Só Admin lê e edita.
app.MapGet("/connector", async (IConnectorProfileStore profiles, ITenantContext tenant, CancellationToken ct) =>
{
    TenantConnectorProfile? profile = await profiles.GetAsync(tenant.TenantId, ct);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPut("/connector", async (ConnectorProfileRequest req, IConnectorProfileStore profiles, ITenantContext tenant, CancellationToken ct) =>
{
    await profiles.UpsertAsync(new TenantConnectorProfile
    {
        TenantId = tenant.TenantId,   // sempre o do usuário; ninguém edita o perfil de outro tenant
        Environment = req.Environment,
        Realtime = req.Realtime,
        InboundAdapter = req.InboundAdapter,
        InboundSettings = req.InboundSettings ?? "{}",
        OutboundAdapter = req.OutboundAdapter,
        OutboundSettings = req.OutboundSettings ?? "{}",
    }, ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.Run();

/// <summary>Corpo do POST /auth/login.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Corpo do PUT /connector. O tenant vem do usuário logado, não do corpo.</summary>
public sealed record ConnectorProfileRequest(
    string Environment,
    bool Realtime,
    string InboundAdapter,
    string? InboundSettings,
    string OutboundAdapter,
    string? OutboundSettings);

/// <summary>Corpo do POST /ingest.</summary>
public sealed record IngestRequest(string TenantId, string NaturalKey, string Locator);

/// <summary>
/// Corpo do POST /integrations/manual. Filial vazia = todas; tenant nulo cai no de dev.
/// <c>DocumentNumber</c> preenchido restringe a uma nota específica (dentro do período).
/// </summary>
public sealed record ManualIntegrationRequest(
    string CompanyCode,
    string? BranchCode,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string? TenantId,
    string? DocumentNumber);

/// <summary>
/// Corpo do POST /schedules. Diário (ScheduledDaily): informe <c>TimeOfDay</c> "HH:mm" (roda D-1).
/// Único (ScheduledOnce): informe <c>RunAt</c> e o par <c>PeriodStart</c>/<c>PeriodEnd</c>.
/// </summary>
public sealed record ScheduleRequest(
    string Mode,
    string CompanyCode,
    string? BranchCode,
    string? TimeOfDay,
    DateTimeOffset? RunAt,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    string? TenantId);
