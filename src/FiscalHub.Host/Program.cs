using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Adapters.Directory.Json;
using FiscalHub.Adapters.Discovery.Local;
using FiscalHub.Adapters.Inbound.Xml;
using FiscalHub.Adapters.Ingress.BlobDrop;
using FiscalHub.Adapters.Ingress.D365Poll;
using FiscalHub.Adapters.Messaging.ServiceBus;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Adapters.Support;
using FiscalHub.Application.Admin;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;
using FiscalHub.Application.Directory;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Integrations;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Queries;
using FiscalHub.Application.Support;
using FiscalHub.Application.Tracing;
using FiscalHub.Application.Validation;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;
using FiscalHub.Host;
using FiscalHub.Infrastructure;
using FiscalHub.Infrastructure.Secrets;
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
// Cofre dos segredos de conector (ADR-0027): Key Vault em produção, o emulador em memória no dev (appsettings.Development).
// Sem a seção, o host não sobe — nunca segue sem cofre.
builder.Services.AddKeyVaultSecretStore(cfg.GetSection("SecretStore").Get<KeyVaultSecretStoreSettings>() ?? new KeyVaultSecretStoreSettings());
// Perfil de conector pela tela: o segredo vai para o cofre, o perfil guarda só a referência, e a leitura nunca o devolve.
builder.Services.AddScoped<ConnectorProfileService>();
// Autenticado por padrão: URL e credencial vêm da seção do ambiente ativo do tenant, e o segredo, do cofre (ADR-0027).
// A seção Avalara (opcional) só ajusta a forma da API — DocumentsPath, TokenPath, margens —, a mesma que a sonda lê.
builder.Services.AddAvalaraComplianceDispatcher(options => cfg.GetSection("Avalara").Bind(options));
builder.Services.AddSupportTicketAdapters();   // chamados: Freshdesk (real) + Local (mock dev)
builder.Services.AddScoped<DocumentTraceQuery>();   // fotos de um documento, só para o tenant de quem está logado (ADR-0028)
builder.Services.AddSingleton<IDocumentValidator<GoodsInvoice>, GoodsInvoiceValidator>();
builder.Services.AddSingleton<IDocumentMetadataExtractor<GoodsInvoice>, GoodsInvoiceMetadataExtractor>();
// A esteira escolhe o source por documento, pela origem da referência (fallback: perfil do tenant) —
// o mesmo tenant recebe XML pelo drop e D365 pelo feed (ADR-0025).
builder.Services.AddScoped(typeof(IInboundSourceResolver<>), typeof(InboundSourceResolver<>));
builder.Services.AddScoped<IDocumentPipeline<GoodsInvoice>, DocumentPipeline<GoodsInvoice>>();
builder.Services.AddScoped<ManualIngestion>();   // /ingest: tenant do login, locator pela regra da origem (ADR-0028)
// Os consumidores de fila entram pelo roteador: NF-e 55 vai à esteira; NFS-e/CT-e e o fora-do-escopo
// constatado na montagem viram o desfecho "ignorado" (ADR-0025).
builder.Services.AddScoped<IDocumentRouter, DocumentRouter>();

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

// Feed de mudanças do D365 (ADR-0024): o worker pergunta ao F&O o que mudou (janela por data, keyset,
// lease por tenant) e publica cada referência na fila de DESCOBERTA. O consumidor dela entra pelo roteador:
// a NF-e 55 é montada pelo source do D365 (4 GETs + cadastros em cache) e segue a mesma esteira; NFS-e e
// CT-e viram "ignorado" (ADR-0025). Poll desligado por padrão: cada tenant liga no perfil (poll.enabled).
builder.Services.AddServiceBusDiscoveryQueue(o => o.QueueName = cfg["ServiceBus:DiscoveryQueue"] ?? "documents-discovered");
builder.Services.AddD365ChangeFeed();
builder.Services.AddD365GoodsInvoiceSource();   // ao lado do source XML; a esteira escolhe pela origem da referência
if (builder.Environment.IsDevelopment())
{
    // Só em dev: token da sessão do Azure CLI (az login), até a app registration existir.
    builder.Services.UseD365AzureCliToken();
}
builder.Services.AddSingleton(new ChangeFeedPollerOptions());
// Registro dos pares (documento, carimbo) já publicados (ADR-0025, D16): singleton de propósito — o poller é
// recriado a cada tick (escopo) e perderia o conjunto; sem ele, cada nota voltaria ~6× pela sobreposição.
builder.Services.AddSingleton<ChangeFeedPublicationLog>();
builder.Services.AddScoped(sp => new ChangeFeedPoller(
    sp.GetRequiredService<IDocumentChangeFeed>(),
    sp.GetRequiredService<IConnectorProfileStore>(),
    sp.GetRequiredService<IChangeFeedCursorStore>(),
    sp.GetRequiredService<ILeaseStore>(),
    sp.GetRequiredKeyedService<IDocumentQueue>(ServiceBusMessagingServiceCollectionExtensions.DiscoveryQueueKey),
    sp.GetRequiredService<ChangeFeedPublicationLog>(),
    sp.GetRequiredService<ChangeFeedPollerOptions>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<ChangeFeedPollingService>();

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
await app.Services.EnsureDevTenantsAsync();
await app.Services.EnsureDevConnectorProfilesAsync();
await app.Services.EnsureDevDocumentsAsync();   // notas de exemplo p/ paginação, KPIs do dia e reprocessar
await app.Services.EnsureDevSchedulesAndExecutionsAsync();   // agendamentos + execuções p/ paginação em Integrações

app.MapGet("/", () =>
    $"FiscalHub host. POST /ingest com {{ naturalKey, locator }}, no tenant do login. XML de exemplo semeado em '{LocalSeed.Locator}'.")
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

// Esqueci a senha: gera o token de redefinição (se a conta existe). Sempre 200 — não vaza se o e-mail
// existe. Em produção enviaria um e-mail; no dev, devolve o token pra completar o fluxo sem servidor SMTP.
app.MapPost("/auth/forgot-password", async (ForgotPasswordRequest req, IPasswordResetService reset, IHostEnvironment env, CancellationToken ct) =>
{
    string? token = await reset.RequestResetAsync(req.Email ?? string.Empty, ct);
    return Results.Ok(new
    {
        message = "Se a conta existir, enviaremos as instruções para redefinir a senha.",
        devToken = env.IsDevelopment() ? token : null,   // só em dev, pra testar sem e-mail
    });
}).AllowAnonymous();

// Redefinir a senha consumindo o token (uso único, com validade).
app.MapPost("/auth/reset-password", async (ResetPasswordRequest req, IPasswordResetService reset, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
    {
        return Results.BadRequest(new { message = "A nova senha precisa ter ao menos 6 caracteres." });
    }

    bool ok = await reset.ResetAsync(req.Token ?? string.Empty, req.NewPassword, ct);
    return ok
        ? Results.Ok(new { message = "Senha redefinida. Você já pode entrar." })
        : Results.BadRequest(new { message = "Link inválido ou expirado. Peça um novo." });
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
// retry e dead-letter ficam por conta do transporte. O tenant é o do usuário logado, e o locator tem de estar
// no espaço de entrada dele (ADR-0028).
app.MapPost("/ingest", async (IngestRequest req, ManualIngestion ingestion, ITenantContext tenant, CancellationToken ct) =>
{
    string? problem = await ingestion.EnqueueAsync(req.NaturalKey, req.Locator, ct);
    return problem is null
        ? Results.Accepted($"/trace/{tenant.TenantId}/{req.NaturalKey}", new { queued = req.NaturalKey })
        : Results.BadRequest(new { message = $"Locator recusado: {problem}" });
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

// Escopado ao tenant logado (ADR-0028): o id de outro tenant dá 404, como no reactivate e no PUT.
app.MapPost("/schedules/{id:int}/deactivate", async (int id, IScheduleStore store, CancellationToken ct) =>
    await store.DeactivateAsync(id, ct) ? Results.NoContent() : Results.NotFound());

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
// Blob. O watcher de ingestão pega, move pro container durável e enfileira — sem /ingest manual. O arquivo cai
// no prefixo do tenant de quem está logado, e nunca no de outro (ADR-0028).
app.MapPost("/drop/{key}", async (string key, string? empresa, BlobServiceClient blobs, ITenantContext tenant, CancellationToken ct) =>
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
    string dropped = $"{tenant.TenantId}/{key}.xml";
    await drop.GetBlobClient(dropped).UploadAsync(content.Content.ToStream(), overwrite: true, ct);

    return Results.Accepted($"/trace/{tenant.TenantId}/{key}", new { dropped });
});

// A mesma resposta para "é de outro tenant" e "não tem fotos": não se confirma a existência de nota alheia.
const string NoTraceMessage = "sem fotos para esse documento.";

// Fotos de rastreabilidade de um documento (fonte, domínio, destino), para o detalhe do dashboard. Só o tenant de
// quem está logado (ADR-0028): outro tenant e documento sem fotos têm o mesmo 404 — não se confirma nota alheia.
app.MapGet("/trace/{tenantId}/{naturalKey}", async (string tenantId, string naturalKey, DocumentTraceQuery traces, CancellationToken ct) =>
{
    IReadOnlyList<TraceFile>? files = await traces.GetAsync(tenantId, naturalKey, ct);
    if (files is null)
    {
        return Results.NotFound(new { message = NoTraceMessage });
    }

    // JSON entra aninhado (legível); a fonte crua (XML) entra como string. A foto repetida em dois períodos
    // (reprocesso em outro mês) vem por último na listagem e prevalece.
    var snapshots = new Dictionary<string, object>();
    foreach (TraceFile file in files)
    {
        snapshots[file.Name] = file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Deserialize<JsonElement>(file.Content)
            : Encoding.UTF8.GetString(file.Content);
    }

    return Results.Ok(snapshots);
});

// Download: zipa as fotos de um documento pra baixar de uma vez, com a mesma regra de tenant do /trace.
app.MapGet("/documents/{tenantId}/{naturalKey}/download", async (string tenantId, string naturalKey, DocumentTraceQuery traces, CancellationToken ct) =>
{
    IReadOnlyList<TraceFile>? files = await traces.GetAsync(tenantId, naturalKey, ct);
    return files is null
        ? Results.NotFound(new { message = NoTraceMessage })
        : Results.File(TraceArchive.Zip(files), "application/zip", $"{naturalKey}.zip");
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

// Perfil de conector do tenant (config de adapters/ambiente/settings). Só Admin lê e edita. O segredo entra pela tela
// como campo de escrita e vai para o cofre; a leitura diz só "configurado" e a data (ADR-0027).
app.MapGet("/connector", async (ConnectorProfileService connector, CancellationToken ct) =>
    await connector.GetAsync(ct) is { } view ? Results.Ok(view) : Results.NotFound())
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPut("/connector", async (ConnectorProfileRequest req, ConnectorProfileService connector, CancellationToken ct) =>
{
    // O tenant é sempre o do usuário (ADR-0028); ninguém edita o perfil de outro tenant.
    ConnectorProfileSaveResult result = await connector.SaveAsync(req, ct);
    return result.Status switch
    {
        ConnectorProfileSaveStatus.Saved => Results.NoContent(),
        ConnectorProfileSaveStatus.Invalid => Results.BadRequest(new { message = result.Message }),
        _ => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status502BadGateway),
    };
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// ---- Administração de usuários (escopada ao tenant do Admin logado) ----
IResult AdminError(AdminStatus status, string? message) => status switch
{
    AdminStatus.NotFound => Results.NotFound(new { message = message ?? "Não encontrado." }),
    AdminStatus.Conflict => Results.Conflict(new { message }),
    _ => Results.BadRequest(new { message }),
};

app.MapGet("/users", async (IUserAdminService users, ITenantContext tenant, CancellationToken ct) =>
    Results.Ok(await users.ListAsync(tenant.TenantId, ct)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/users", async (CreateUserRequest req, IUserAdminService users, ITenantContext tenant, CancellationToken ct) =>
{
    AdminResult<AdminUserView> r = await users.CreateAsync(
        tenant.TenantId, new CreateUserInput(req.Email, req.Name, req.Role, req.Password), ct);
    return r.Status == AdminStatus.Ok ? Results.Ok(r.Value) : AdminError(r.Status, r.Message);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPut("/users/{id:int}", async (int id, UpdateUserRequest req, IUserAdminService users, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct) =>
{
    // Anti-lockout: o Admin não pode se rebaixar nem se desativar (evita ficar sem acesso).
    bool isSelf = int.TryParse(principal.FindFirstValue("sub"), out int me) && me == id;
    if (isSelf && (req.Active == false || (req.Role is not null && req.Role != "Admin")))
    {
        return Results.BadRequest(new { message = "Você não pode desativar nem rebaixar a própria conta." });
    }

    AdminResult<AdminUserView> r = await users.UpdateAsync(
        tenant.TenantId, id, new UpdateUserInput(req.Name, req.Role, req.Active), ct);
    return r.Status == AdminStatus.Ok ? Results.Ok(r.Value) : AdminError(r.Status, r.Message);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/users/{id:int}/reset-password", async (int id, ResetUserPasswordRequest req, IUserAdminService users, ITenantContext tenant, CancellationToken ct) =>
{
    AdminStatus status = await users.ResetPasswordAsync(tenant.TenantId, id, req.NewPassword ?? string.Empty, ct);
    return status == AdminStatus.Ok
        ? Results.NoContent()
        : AdminError(status, status == AdminStatus.Invalid ? "A senha precisa ter ao menos 6 caracteres." : "Usuário não encontrado.");
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// ---- Cadastro do tenant corrente ----
app.MapGet("/tenant", async (ITenantAdminService tenants, ITenantContext tenant, CancellationToken ct) =>
{
    TenantView? view = await tenants.GetAsync(tenant.TenantId, ct);
    // Sem registro ainda: devolve um esqueleto com o slug, pra tela poder preencher e salvar.
    return Results.Ok(view ?? new TenantView(tenant.TenantId, tenant.TenantId, null, true));
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPut("/tenant", async (UpdateTenantRequest req, ITenantAdminService tenants, ITenantContext tenant, CancellationToken ct) =>
{
    AdminResult<TenantView> r = await tenants.UpdateAsync(tenant.TenantId, new UpdateTenantInput(req.Name, req.Cnpj), ct);
    return r.Status == AdminStatus.Ok ? Results.Ok(r.Value) : AdminError(r.Status, r.Message);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// ---- Abrir chamado de suporte para uma ou mais notas (qualquer usuário; escopado ao tenant) ----
// multipart/form-data: subject, description, naturalKeys (repetido) e files (anexos extras opcionais).
app.MapPost("/support/tickets", async (HttpRequest request, ISupportTicketService support, ITenantContext tenant, CancellationToken ct) =>
{
    IFormCollection form = await request.ReadFormAsync(ct);
    string subject = form["subject"].ToString();
    string description = form["description"].ToString();
    List<string> keys = form["naturalKeys"].Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!).ToList();

    var extras = new List<TicketAttachment>();
    foreach (IFormFile file in form.Files)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        extras.Add(new TicketAttachment(
            file.FileName,
            ms.ToArray(),
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType));
    }

    try
    {
        TicketResult result = await support.OpenAsync(tenant.TenantId, keys, subject, description, extras, ct);
        return Results.Ok(new { ticketId = result.Id, url = result.Url });
    }
    catch (SupportTicketException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).DisableAntiforgery();

// Estimativa do tamanho dos anexos automáticos (zips de logs) das notas — pra tela mostrar o disponível.
app.MapPost("/support/tickets/estimate", async (EstimateTicketRequest req, ISupportTicketService support, ITenantContext tenant, CancellationToken ct) =>
{
    long bytes = await support.EstimateLogsBytesAsync(tenant.TenantId, req.NaturalKeys ?? [], ct);
    return Results.Ok(new { logsBytes = bytes, limitBytes = 20L * 1024 * 1024 });
});

app.Run();

/// <summary>Corpo do POST /auth/login.</summary>
public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string? Email);
public sealed record ResetPasswordRequest(string? Token, string? NewPassword);

/// <summary>Corpos dos endpoints de administração de usuários/tenant. O tenant vem sempre do claim.</summary>
public sealed record CreateUserRequest(string Email, string Name, string Role, string Password);
public sealed record UpdateUserRequest(string? Name, string? Role, bool? Active);
public sealed record ResetUserPasswordRequest(string? NewPassword);
public sealed record UpdateTenantRequest(string Name, string? Cnpj);

/// <summary>Corpo do POST /support/tickets/estimate — só as notas, pra estimar o tamanho dos logs.</summary>
public sealed record EstimateTicketRequest(IReadOnlyList<string>? NaturalKeys);

/// <summary>Corpo do POST /ingest. O tenant vem do usuário logado, não do corpo (ADR-0028).</summary>
public sealed record IngestRequest(string NaturalKey, string Locator);

/// <summary>
/// Corpo do POST /integrations/manual. Filial vazia = todas. O tenant é o do usuário logado, e não vem do corpo
/// (ADR-0028). <c>DocumentNumber</c> preenchido restringe a uma nota específica (dentro do período).
/// </summary>
public sealed record ManualIntegrationRequest(
    string CompanyCode,
    string? BranchCode,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string? DocumentNumber);

/// <summary>
/// Corpo do POST /schedules. Diário (ScheduledDaily): informe <c>TimeOfDay</c> "HH:mm" (roda D-1).
/// Único (ScheduledOnce): informe <c>RunAt</c> e o par <c>PeriodStart</c>/<c>PeriodEnd</c>. O tenant é o do usuário
/// logado, e não vem do corpo (ADR-0028).
/// </summary>
public sealed record ScheduleRequest(
    string Mode,
    string CompanyCode,
    string? BranchCode,
    string? TimeOfDay,
    DateTimeOffset? RunAt,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd);
