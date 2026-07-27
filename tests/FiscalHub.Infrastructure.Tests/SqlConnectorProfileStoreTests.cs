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

        await h.Store.UpsertAsync(Profile("Sandbox", realtime: true));
        TenantConnectorProfile? created = await h.Store.GetAsync("tenant-a");
        Assert.Equal("Sandbox", created!.Environment);
        Assert.True(created.Realtime);
        Assert.Equal("Avalara", created.OutboundAdapter);

        await h.Store.UpsertAsync(Profile("Production", realtime: false));   // atualiza o mesmo tenant
        TenantConnectorProfile? updated = await h.Store.GetAsync("tenant-a");
        Assert.Equal("Production", updated!.Environment);
        Assert.False(updated.Realtime);
        Assert.Equal(1, await h.Db.ConnectorProfiles.CountAsync());   // upsert: uma linha só
    }

    private static TenantConnectorProfile Profile(string environment, bool realtime) => new()
    {
        TenantId = "tenant-a",
        Environment = environment,
        Realtime = realtime,
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
