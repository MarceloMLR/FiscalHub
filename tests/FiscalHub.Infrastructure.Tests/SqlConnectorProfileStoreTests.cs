using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Especifica o store do perfil de conector: grava, lê e faz upsert (um por tenant).</summary>
public class SqlConnectorProfileStoreTests
{
    [Fact]
    public async Task Upserts_and_reads_profile_without_duplicating()
    {
        using var h = NewStore();

        Assert.Null(await h.Store.GetAsync("tenant-a"));   // ainda não existe

        await h.Store.UpsertAsync(Profile("Sandbox"));
        TenantConnectorProfile? created = await h.Store.GetAsync("tenant-a");
        Assert.Equal("Sandbox", created!.Environment);
        Assert.Equal("Avalara", created.OutboundAdapter);

        await h.Store.UpsertAsync(Profile("Production"));   // atualiza o mesmo tenant
        TenantConnectorProfile? updated = await h.Store.GetAsync("tenant-a");
        Assert.Equal("Production", updated!.Environment);
        Assert.Equal(1, await h.Db.ConnectorProfiles.CountAsync());   // upsert: uma linha só
    }

    [Fact]
    public async Task Lists_profiles_by_inbound_adapter_across_tenants()
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile("Sandbox") with { TenantId = "tenant-c" });
        await h.Store.UpsertAsync(Profile("Sandbox"));   // tenant-a, Dynamics365
        await h.Store.UpsertAsync(Profile("Sandbox") with { TenantId = "tenant-b", InboundAdapter = "iScala" });

        IReadOnlyList<TenantConnectorProfile> d365 = await h.Store.ListByInboundAdapterAsync("Dynamics365");

        Assert.Equal(["tenant-a", "tenant-c"], d365.Select(p => p.TenantId));   // só o adapter pedido, em ordem
        Assert.Empty(await h.Store.ListByInboundAdapterAsync("Xml"));
    }

    [Fact]
    public async Task Modules_go_and_come_back_and_null_stays_null()
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile("Sandbox") with { Modules = ["Fiscal", "Inventario"] });
        await h.Store.UpsertAsync(Profile("Sandbox") with { TenantId = "tenant-c" });

        Assert.Equal(["Fiscal", "Inventario"], (await h.Store.GetAsync("tenant-a"))!.Modules);
        Assert.Equal("[\"Fiscal\",\"Inventario\"]", (await h.Db.ConnectorProfiles.SingleAsync(p => p.TenantId == "tenant-a")).Modules);
        Assert.Null((await h.Store.GetAsync("tenant-c"))!.Modules);   // nada gravado: vale o padrão, só o Fiscal
        Assert.Equal(["Fiscal"], TenantModules.Of(await h.Store.GetAsync("tenant-c")));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[\"Fiscal\",\"Folha\"]")]
    [InlineData("[]")]
    public async Task Unreadable_modules_put_by_sql_fall_back_to_the_default(string column)
    {
        using var h = NewStore();
        await h.Store.UpsertAsync(Profile("Sandbox"));
        ConnectorProfileRow row = await h.Db.ConnectorProfiles.SingleAsync();
        row.Modules = column;
        await h.Db.SaveChangesAsync();

        Assert.Equal(["Fiscal"], TenantModules.Of(await h.Store.GetAsync("tenant-a")));
    }

    private static TenantConnectorProfile Profile(string environment) => new()
    {
        TenantId = "tenant-a",
        Environment = environment,
        InboundAdapter = "Dynamics365",
        OutboundAdapter = "Avalara",
    };

    private static Harness NewStore()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options;
        var db = new ProcessingDbContext(options);
        db.Database.EnsureCreated();
        return new Harness(db, conn, new SqlConnectorProfileStore(db));
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlConnectorProfileStore store) : IDisposable
    {
        public ProcessingDbContext Db => db;
        public SqlConnectorProfileStore Store => store;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
