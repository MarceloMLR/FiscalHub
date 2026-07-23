using Azure.Storage.Blobs;
using FiscalHub.Application.Pipeline;
using FiscalHub.Application.Tracing;
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

    /// <summary>Cria o schema do store se ainda não existir (dev local; produção usa migrations).</summary>
    public static async Task EnsureProcessingSchemaAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }
}
