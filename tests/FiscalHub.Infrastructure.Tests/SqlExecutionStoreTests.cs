using FiscalHub.Application.Integrations;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Especifica o store/queries de execuções: grava e lista as execuções, mais recentes primeiro.</summary>
public class SqlExecutionStoreTests
{
    [Fact]
    public async Task Records_and_lists_executions_newest_first()
    {
        using var h = NewStore();

        await h.Store.RecordAsync(Exec(IntegrationMode.Manual, 2));
        await h.Store.RecordAsync(Exec(IntegrationMode.ScheduledDaily, 5));

        var list = await h.Queries.ListRecentAsync(10);

        Assert.Equal(2, list.Count);
        Assert.Equal(IntegrationMode.ScheduledDaily, list[0].Mode);   // mais recente primeiro
        Assert.Equal(5, list[0].DiscoveredCount);
        Assert.Equal("2026-06-01", list[0].PeriodStart);             // guardado como yyyy-MM-dd
        Assert.Equal(IntegrationMode.Manual, list[1].Mode);
    }

    private static IntegrationExecution Exec(IntegrationMode mode, int count) => new()
    {
        Mode = mode,
        TenantId = "tenant-a",
        CompanyCode = "12345678",
        BranchCode = null,
        PeriodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        PeriodEnd = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
        DiscoveredCount = count,
    };

    private static Harness NewStore()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options;
        var db = new ProcessingDbContext(options);
        db.Database.EnsureCreated();
        return new Harness(db, conn, new SqlExecutionStore(db, TimeProvider.System), new SqlExecutionQueries(db));
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlExecutionStore store, SqlExecutionQueries queries) : IDisposable
    {
        public SqlExecutionStore Store => store;
        public SqlExecutionQueries Queries => queries;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
