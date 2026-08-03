using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FiscalHub.Application.Admin;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Integrations;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Queries;
using FiscalHub.Application.Support;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Envelope;
using FiscalHub.Infrastructure.Admin;
using FiscalHub.Infrastructure.Auth;
using FiscalHub.Infrastructure.Persistence;
using FiscalHub.Infrastructure.Support;
using FiscalHub.Infrastructure.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FiscalHub.Infrastructure;

/// <summary>Registro no DI da infraestrutura de persistência.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registra o <c>IProcessingStore</c> em SQL Server (EF Core).</summary>
    public static IServiceCollection AddSqlProcessingStore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ProcessingDbContext>(options => options.UseSqlServer(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IProcessingStore, SqlProcessingStore>();
        services.AddScoped<IDocumentQueries, SqlDocumentQueries>();
        services.AddScoped<IExecutionStore, SqlExecutionStore>();
        services.AddScoped<IExecutionQueries, SqlExecutionQueries>();
        services.AddScoped<IScheduleStore, SqlScheduleStore>();
        services.AddScoped<IUserAuthenticator, SqlUserAuthenticator>();
        services.AddScoped<IPasswordResetService, SqlPasswordResetService>();
        services.AddScoped<IConnectorProfileStore, SqlConnectorProfileStore>();
        services.AddScoped<IUserAdminService, SqlUserAdminService>();
        services.AddScoped<ITenantAdminService, SqlTenantAdminService>();
        services.AddScoped<INoteTraceReader, BlobNoteTraceReader>();
        services.AddScoped<ISupportTicketService, SupportTicketService>();
        return services;
    }

    /// <summary>
    /// Registra a rastreabilidade em Blob (fotos domínio/destino). Requer um <c>BlobServiceClient</c>
    /// já registrado. A retenção é controlada por lifecycle policy no container, não por código.
    /// </summary>
    public static IServiceCollection AddBlobProcessingTrace(this IServiceCollection services, string containerName)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IProcessingTrace>(sp => new BlobProcessingTrace(
            sp.GetRequiredService<BlobServiceClient>(),
            containerName,
            sp.GetRequiredService<TimeProvider>())));
        return services;
    }

    /// <summary>
    /// Aplica as migrations pendentes (cria o schema se não existe, evolui se já existe). Substitui
    /// o antigo EnsureCreated — que não migrava schema existente e exigia dropar o banco a cada mudança.
    /// </summary>
    public static async Task MigrateProcessingSchemaAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Semeia usuários de dev (uma vez, se a tabela está vazia): um admin no tenant-a (vê os dados
    /// semeados) e um viewer no tenant-b (não vê nada — demonstra o isolamento por tenant). Senha
    /// com hash; em produção isso viria de um cadastro real, não de seed.
    /// </summary>
    public static async Task EnsureDevUsersAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        string hash = Pbkdf2PasswordHasher.Hash("Fiscal@123");
        db.Users.AddRange(
            new UserRow { Email = "admin@fiscalhub.local", Name = "Marcelo Lima", PasswordHash = hash, TenantId = "tenant-a", Role = "Admin" },
            // Colegas de time no tenant-a: exercitam a tela de usuários (papéis e ativo/inativo variados).
            new UserRow { Email = "ana.souza@fiscalhub.local", Name = "Ana Souza", PasswordHash = hash, TenantId = "tenant-a", Role = "Admin" },
            new UserRow { Email = "carlos.dias@fiscalhub.local", Name = "Carlos Dias", PasswordHash = hash, TenantId = "tenant-a", Role = "Viewer" },
            new UserRow { Email = "julia.martins@fiscalhub.local", Name = "Júlia Martins", PasswordHash = hash, TenantId = "tenant-a", Role = "Viewer" },
            new UserRow { Email = "pedro.rocha@fiscalhub.local", Name = "Pedro Rocha", PasswordHash = hash, TenantId = "tenant-a", Role = "Viewer", Active = false },
            new UserRow { Email = "beta@fiscalhub.local", Name = "Beta Viewer", PasswordHash = hash, TenantId = "tenant-b", Role = "Viewer" });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Semeia o cadastro (fino) dos tenants de dev, uma vez. No modelo por-cliente cada tenant é a
    /// config de uma instância; aqui só guardamos identidade + dados cadastrais para a tela de admin.
    /// </summary>
    public static async Task EnsureDevTenantsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        TimeProvider clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        if (await db.Tenants.AnyAsync(ct))
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        db.Tenants.AddRange(
            new TenantRow { TenantId = "tenant-a", Name = "ACME Fiscal Ltda", Cnpj = "12.345.678/0001-90", Active = true, CreatedAt = now },
            new TenantRow { TenantId = "tenant-b", Name = "Beta Comércio S.A.", Cnpj = "98.765.432/0001-21", Active = true, CreatedAt = now });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Semeia perfis de conector de dev (uma vez): tenant-a via Dynamics 365 com tempo real, tenant-b
    /// via iScala sem tempo real — os dois enviando pra Avalara com credenciais próprias por ambiente.
    /// Segredos entram como referência (kv:...), nunca o valor cru — em produção resolvidos no Key Vault.
    /// </summary>
    public static async Task EnsureDevConnectorProfilesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

        if (await db.ConnectorProfiles.AnyAsync(ct))
        {
            return;
        }

        db.ConnectorProfiles.AddRange(
            new ConnectorProfileRow
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = "Dynamics365",
                InboundSettings = """{"url":"https://erp-a.crm.dynamics.com/","clientIdRef":"kv:d365-a-clientid","clientSecretRef":"kv:d365-a-secret"}""",
                OutboundAdapter = "Avalara",
                OutboundSettings = """{"sandbox":{"baseUrl":"http://localhost:5100/","clientSecretRef":"kv:avalara-a-sandbox-secret","clientTokenRef":"kv:avalara-a-sandbox-token"},"production":{"baseUrl":"https://api.avalara.com/","clientSecretRef":"kv:avalara-a-prod-secret","clientTokenRef":"kv:avalara-a-prod-token"}}""",
                // Chamados: mock local pra demo (funciona sem conta). Troque p/ "Freshdesk" + domain/apiKey na tela de Configurações.
                SupportAdapter = "Local",
                SupportSettings = """{"domain":"suaempresa.freshdesk.com","apiKeyRef":"kv:freshdesk-a-key","requesterEmail":"suporte@acme.com","priority":2}""",
            },
            new ConnectorProfileRow
            {
                TenantId = "tenant-b",
                Environment = "Sandbox",
                Realtime = false,   // iScala deste cliente não faz evento — só agendado/manual
                InboundAdapter = "iScala",
                InboundSettings = """{"host":"iscala-b.local","company":"B01","userRef":"kv:iscala-b-user","passwordRef":"kv:iscala-b-pass"}""",
                OutboundAdapter = "Avalara",
                OutboundSettings = """{"sandbox":{"baseUrl":"http://localhost:5100/","clientSecretRef":"kv:avalara-b-sandbox-secret","clientTokenRef":"kv:avalara-b-sandbox-token"},"production":{"baseUrl":"https://api.avalara.com/","clientSecretRef":"kv:avalara-b-prod-secret","clientTokenRef":"kv:avalara-b-prod-token"}}""",
            });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Semeia documentos de exemplo (uma vez, se a tabela está vazia) só no tenant-a: dezenas de
    /// grupos ao longo dos últimos dias — incluindo o dia de hoje (alimenta os KPIs) — com status
    /// variados para exercitar paginação, filtros e o reprocessamento. As DUAS notas reais do
    /// catálogo (123 e 456) entram como falha e são reprocessáveis de verdade (rebuscam o blob).
    /// </summary>
    public static async Task EnsureDevDocumentsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

        if (await db.ProcessedDocuments.AnyAsync(ct))
        {
            return;
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTime todayLocal = DateTimeOffset.Now.Date;   // mesmo "hoje" do navegador (dev na mesma máquina)

        // Distribuição pesada em Confirmado, com uma pitada de cada falha (para KPI/filtro).
        IntegrationStatus[] cycle =
        [
            IntegrationStatus.Confirmed, IntegrationStatus.Confirmed, IntegrationStatus.Confirmed,
            IntegrationStatus.Submitted, IntegrationStatus.Pending,
            IntegrationStatus.IntegrationError, IntegrationStatus.Unconfirmed, IntegrationStatus.DeadLettered,
        ];
        (string Company, string Branch)[] orgs =
        [
            ("12345678", "0001"), ("12345678", "0002"), ("98765432", "0001"),
            ("98765432", "0003"), ("11222333", "0001"), ("44556677", "0002"),
        ];
        // Modo/gatilho da integração — variado para a coluna "Tipo" mostrar Tempo real / Imediata / Diária / Agendada.
        string[] triggers = ["RealTime", "RealTime", "Manual", "ScheduledDaily", "ScheduledOnce"];

        // Container das fotos de rastreabilidade (origem/domínio/destino) — semeadas junto com as notas.
        BlobContainerClient traces = scope.ServiceProvider.GetRequiredService<BlobServiceClient>().GetBlobContainerClient("traces");
        await traces.CreateIfNotExistsAsync(cancellationToken: ct);
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        var rows = new List<ProcessedDocument>();
        int seq = 0;
        for (int day = 0; day < 7; day++)   // hoje e os 6 dias anteriores → 42 grupos (paginação)
        {
            DateTime date = todayLocal.AddDays(-day);
            string refDate = date.ToString("yyyy-MM-dd");
            foreach ((string company, string branch) in orgs)
            {
                int docs = 1 + ((day + branch[3]) % 3);   // 1..3 notas por grupo
                for (int k = 0; k < docs; k++)
                {
                    IntegrationStatus status = cycle[seq % cycle.Length];
                    string key = $"3526{company}5500100000{seq:D6}";
                    string number = (1000 + seq).ToString();
                    string? reason = status switch
                    {
                        IntegrationStatus.IntegrationError => "Rejeitada pelo compliance (regra de tributação).",
                        IntegrationStatus.DeadLettered => "Falha definitiva após as retentativas.",
                        IntegrationStatus.Unconfirmed => "Sem retorno do compliance após várias consultas.",
                        _ => null,
                    };

                    rows.Add(new ProcessedDocument
                    {
                        TenantId = "tenant-a",
                        NaturalKey = key,
                        Type = DocumentType.GoodsInvoice55,
                        Status = status,
                        CompanyCode = company,
                        BranchCode = branch,
                        ReferenceDate = refDate,
                        DocumentNumber = number,
                        DocumentModel = "55",
                        Trigger = triggers[seq % triggers.Length],
                        Attempts = status == IntegrationStatus.Unconfirmed ? 6 : status == IntegrationStatus.Submitted ? 2 : 1,
                        Reason = reason,
                        CreatedAt = nowUtc.AddDays(-day),
                        UpdatedAt = nowUtc.AddDays(-day).AddMinutes(seq),
                    });

                    // Origem/domínio/destino no Blob, como no fluxo real — assim o detalhe da nota tem os 3 arquivos.
                    await WriteDevTraceAsync(traces, key, date.ToString("yyyyMM"), company, branch, number, refDate, status, reason, jsonOpts, ct);
                    seq++;
                }
            }
        }

        // As duas notas REAIS do catálogo, em falha → reprocessáveis (o adapter rebusca o blob e reintegra).
        rows.Add(new ProcessedDocument
        {
            TenantId = "tenant-a",
            NaturalKey = "35260612345678000190550010000001231000000123",
            Type = DocumentType.GoodsInvoice55,
            Status = IntegrationStatus.IntegrationError,
            CompanyCode = "12345678", BranchCode = "0001", ReferenceDate = "2026-06-01",
            DocumentNumber = "123", DocumentModel = "55", Attempts = 1, Trigger = "RealTime",
            Reason = "Rejeitada pelo compliance — reprocessar após ajuste.",
            CreatedAt = nowUtc, UpdatedAt = nowUtc,
        });
        rows.Add(new ProcessedDocument
        {
            TenantId = "tenant-a",
            NaturalKey = "35260698765432000188550010000004561000000456",
            Type = DocumentType.GoodsInvoice55,
            Status = IntegrationStatus.DeadLettered,
            CompanyCode = "98765432", BranchCode = "0001", ReferenceDate = "2026-06-02",
            DocumentNumber = "456", DocumentModel = "55", Attempts = 3, Trigger = "ScheduledDaily",
            Reason = "Dead-letter — exige intervenção; reprocessar.",
            CreatedAt = nowUtc, UpdatedAt = nowUtc,
        });

        db.ProcessedDocuments.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Semeia agendamentos e execuções de exemplo (uma vez, por tabela vazia) no tenant-a — dezenas
    /// de cada, com modos/empresas/datas variados, para exercitar a paginação da tela de Integrações.
    /// </summary>
    public static async Task EnsureDevSchedulesAndExecutionsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        (string Company, string Branch)[] orgs =
        [
            ("12345678", "0001"), ("12345678", "0002"), ("98765432", "0001"),
            ("98765432", "0003"), ("11222333", "0001"), ("44556677", "0002"),
        ];

        if (!await db.ScheduledIntegrations.AnyAsync(ct))
        {
            var rows = new List<ScheduledIntegrationRow>();
            for (int i = 0; i < 28; i++)
            {
                bool daily = i % 2 == 0;
                (string company, string branch) = orgs[i % orgs.Length];
                DateTimeOffset next = nowUtc.AddHours(6 + i).AddDays(daily ? 0 : i % 12);
                rows.Add(new ScheduledIntegrationRow
                {
                    Mode = daily ? IntegrationMode.ScheduledDaily : IntegrationMode.ScheduledOnce,
                    TenantId = "tenant-a",
                    CompanyCode = company,
                    BranchCode = i % 4 == 0 ? null : branch,   // null = todas as filiais
                    PeriodStart = daily ? null : "2026-07-01",
                    PeriodEnd = daily ? null : "2026-07-15",
                    NextRunTicks = next.UtcTicks,
                    Active = i % 3 != 0,   // ~2/3 ativos, o resto pausado
                    CreatedAt = nowUtc.AddDays(-i),
                });
            }

            db.ScheduledIntegrations.AddRange(rows);
        }

        if (!await db.IntegrationExecutions.AnyAsync(ct))
        {
            IntegrationMode[] modes = [IntegrationMode.Manual, IntegrationMode.ScheduledDaily, IntegrationMode.ScheduledOnce];
            var rows = new List<IntegrationExecutionRow>();
            for (int i = 0; i < 45; i++)
            {
                (string company, string branch) = orgs[i % orgs.Length];
                DateTimeOffset when = nowUtc.AddHours(-(i * 7));   // do mais recente ao mais antigo
                rows.Add(new IntegrationExecutionRow
                {
                    Mode = modes[i % modes.Length],
                    TenantId = "tenant-a",
                    CompanyCode = company,
                    BranchCode = i % 5 == 0 ? null : branch,
                    PeriodStart = when.AddDays(-1).ToString("yyyy-MM-dd"),
                    PeriodEnd = when.ToString("yyyy-MM-dd"),
                    DiscoveredCount = (i * 13) % 400,
                    CreatedAt = when,
                });
            }

            db.IntegrationExecutions.AddRange(rows);
        }

        await db.SaveChangesAsync(ct);
    }

    // Grava as fotos de rastreabilidade (origem/domínio/destino) de uma nota semeada, no mesmo layout
    // do fluxo real: {tenant}/{aaaaMM}/{chave}/source.xml · domain.json · avalara.json.
    private static async Task WriteDevTraceAsync(
        BlobContainerClient traces, string key, string period, string company, string branch,
        string number, string refDate, IntegrationStatus status, string? reason, JsonSerializerOptions jsonOpts, CancellationToken ct)
    {
        string source = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="NFe{key}" versao="4.00">
                  <ide><mod>55</mod><serie>1</serie><nNF>{number}</nNF><dhEmi>{refDate}T10:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>{company}000190</CNPJ><xNome>Empresa {company} LTDA</xNome></emit>
                  <det nItem="1"><prod><cProd>PROD-{number}</cProd><xProd>Produto de exemplo</xProd><vProd>250.00</vProd></prod></det>
                  <total><ICMSTot><vNF>250.00</vNF></ICMSTot></total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;

        string domain = JsonSerializer.Serialize(
            new { tenantId = "tenant-a", naturalKey = key, company, branch, number, model = "55", issuedAt = $"{refDate}T10:00:00-03:00", totalAmount = 250.00m, status = status.ToString() },
            jsonOpts);

        string outbound = JsonSerializer.Serialize(
            new { destination = "Avalara", documentCode = $"AVL-{number}", status = status.ToString(), messages = new[] { reason ?? "Documento integrado com sucesso." } },
            jsonOpts);

        await UploadTraceAsync(traces, $"tenant-a/{period}/{key}/source.xml", "application/xml", source, ct);
        await UploadTraceAsync(traces, $"tenant-a/{period}/{key}/domain.json", "application/json", domain, ct);
        await UploadTraceAsync(traces, $"tenant-a/{period}/{key}/avalara.json", "application/json", outbound, ct);
    }

    private static async Task UploadTraceAsync(BlobContainerClient container, string path, string contentType, string content, CancellationToken ct)
    {
        BlobClient blob = container.GetBlobClient(path);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
    }
}
