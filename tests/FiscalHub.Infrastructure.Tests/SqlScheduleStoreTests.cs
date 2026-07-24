using FiscalHub.Application.Integrations;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Especifica o store de agendamentos: cria, lista, filtra vencidos, reprograma e desativa.</summary>
public class SqlScheduleStoreTests
{
    [Fact]
    public async Task Creates_lists_and_filters_due_then_reschedules_and_deactivates()
    {
        using var h = NewStore();
        var past = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        var future = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);

        int dueId = await h.Store.CreateAsync(Daily(past));
        await h.Store.CreateAsync(Daily(future));

        Assert.Equal(2, (await h.Store.ListAsync()).Count);

        var due = await h.Store.ListDueAsync(new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));
        Assert.Single(due);                       // só o vencido
        Assert.Equal(dueId, due[0].Id);

        await h.Store.RescheduleAsync(dueId, past.AddDays(1));
        ScheduledIntegration rescheduled = (await h.Store.ListAsync()).First(s => s.Id == dueId);
        Assert.Equal(past.AddDays(1), rescheduled.NextRunAt);   // instante igual (DateTimeOffset compara UTC)
        Assert.True(rescheduled.Active);

        await h.Store.DeactivateAsync(dueId);
        Assert.False((await h.Store.ListAsync()).First(s => s.Id == dueId).Active);
    }

    [Fact]
    public async Task Reschedule_with_null_deactivates()
    {
        using var h = NewStore();
        int id = await h.Store.CreateAsync(Daily(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));

        await h.Store.RescheduleAsync(id, null);   // caso único: cumpriu

        Assert.False((await h.Store.ListAsync()).First(s => s.Id == id).Active);
    }

    private static ScheduledIntegration Daily(DateTimeOffset nextRun) => new()
    {
        Mode = IntegrationMode.ScheduledDaily,
        TenantId = "tenant-a",
        CompanyCode = "12345678",
        NextRunAt = nextRun,
    };

    private static Harness NewStore()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options;
        var db = new ProcessingDbContext(options);
        db.Database.EnsureCreated();
        return new Harness(db, conn, new SqlScheduleStore(db, TimeProvider.System));
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlScheduleStore store) : IDisposable
    {
        public SqlScheduleStore Store => store;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
