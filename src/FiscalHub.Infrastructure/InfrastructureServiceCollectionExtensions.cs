using FiscalHub.Application.Pipeline;
using FiscalHub.Infrastructure.Persistence;
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

    /// <summary>Cria o schema do store se ainda não existir (dev local; produção usa migrations).</summary>
    public static async Task EnsureProcessingSchemaAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }
}
