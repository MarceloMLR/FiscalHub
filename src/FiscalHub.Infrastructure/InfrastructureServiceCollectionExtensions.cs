using Azure.Storage.Blobs;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Integrations;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Queries;
using FiscalHub.Application.Tracing;
using FiscalHub.Infrastructure.Auth;
using FiscalHub.Infrastructure.Persistence;
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
        services.AddScoped<IConnectorProfileStore, SqlConnectorProfileStore>();
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

        db.Users.AddRange(
            new UserRow
            {
                Email = "admin@fiscalhub.local",
                Name = "Marcelo Lima",
                PasswordHash = Pbkdf2PasswordHasher.Hash("Fiscal@123"),
                TenantId = "tenant-a",
                Role = "Admin",
            },
            new UserRow
            {
                Email = "beta@fiscalhub.local",
                Name = "Beta Viewer",
                PasswordHash = Pbkdf2PasswordHasher.Hash("Fiscal@123"),
                TenantId = "tenant-b",
                Role = "Viewer",
            });

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
}
