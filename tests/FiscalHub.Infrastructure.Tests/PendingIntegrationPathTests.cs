using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// O caminho que a migração não sabe vem do host, uma vez (change <c>platform-account-and-connectors</c>, D10 e 6.10): na
/// subida, o caminho nulo da integração de notas — a marca que a M3 deixa — é preenchido com o que o host tem. Só o nulo:
/// o preenchido não é trocado, e o vazio que a tela grava é a integração inativa que alguém escolheu.
/// </summary>
public class PendingIntegrationPathTests
{
    [Fact]
    public async Task Only_the_null_path_of_the_integration_of_the_adapter_is_filled_and_once()
    {
        using var h = await HarnessAsync();
        await SaveAsync(h, "tenant-a", "Avalara", ConnectorIntegrations.Invoices, null);                          // à espera
        await SaveAsync(h, "tenant-b", "Avalara", ConnectorIntegrations.Invoices, "");                            // inativa, da tela
        await SaveAsync(h, "tenant-c", "Avalara", ConnectorIntegrations.Invoices, "caminho/da/tela");             // preenchido
        await SaveAsync(h, "tenant-d", "Outro", ConnectorIntegrations.Invoices, null);                            // outro adapter
        await SaveAsync(h, "tenant-e", "Avalara", ConnectorIntegrations.ChartOfAccounts, null);                   // outra integração

        int filled = await h.Services.FillPendingIntegrationPathAsync("Avalara", ConnectorIntegrations.Invoices, "taxcompliance/v2/fiscal/dfe");
        int again = await h.Services.FillPendingIntegrationPathAsync("Avalara", ConnectorIntegrations.Invoices, "documents");

        Assert.Equal(1, filled);
        Assert.Equal(0, again);   // uma vez: preenchido, o caminho é da tela
        Assert.Equal("taxcompliance/v2/fiscal/dfe", await EndpointAsync(h, "tenant-a"));
        Assert.Equal("", await EndpointAsync(h, "tenant-b"));
        Assert.Equal("caminho/da/tela", await EndpointAsync(h, "tenant-c"));
        Assert.Null(await EndpointAsync(h, "tenant-d"));
        Assert.Null(await EndpointAsync(h, "tenant-e"));
    }

    private static async Task SaveAsync(Harness h, string tenant, string adapter, string key, string? endpoint)
    {
        await using AsyncServiceScope scope = h.Services.CreateAsyncScope();
        ProcessingDbContext db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        db.PlatformAccounts.Add(new PlatformAccountRow
        {
            TenantId = tenant,
            Platform = "Avalara",
            Connectors =
            [
                new ConnectorRow
                {
                    Adapter = adapter,
                    Settings = "{}",
                    Modules = [new ConnectorModuleRow { Module = "Fiscal", Integrations = [new ConnectorIntegrationRow { TenantId = tenant, Key = key, Endpoint = endpoint }] }],
                },
            ],
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> EndpointAsync(Harness h, string tenant)
    {
        await using AsyncServiceScope scope = h.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessingDbContext>().Set<ConnectorIntegrationRow>()
            .Where(i => i.TenantId == tenant).Select(i => i.Endpoint).SingleAsync();
    }

    private static async Task<Harness> HarnessAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        ServiceProvider services = new ServiceCollection()
            .AddDbContext<ProcessingDbContext>(o => o.UseSqlite(connection))
            .BuildServiceProvider();
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessingDbContext>().Database.EnsureCreatedAsync();
        }

        return new Harness(services, connection);
    }

    private sealed class Harness(ServiceProvider services, SqliteConnection connection) : IDisposable
    {
        public ServiceProvider Services => services;

        public void Dispose()
        {
            services.Dispose();
            connection.Dispose();
        }
    }
}
