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
    public async Task Deactivate_by_another_tenant_is_not_found_and_leaves_the_schedule_active()
    {
        using var h = NewStore();
        int id = await h.Store.CreateAsync(Daily(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));

        bool found = await h.StoreFor("tenant-b").DeactivateAsync(id);   // o id é do tenant-a (ADR-0028)

        Assert.False(found);
        Assert.True((await h.Store.ListAsync()).First(s => s.Id == id).Active);
        Assert.True(await h.Store.DeactivateAsync(id));                  // o dono desativa
    }

    [Fact]
    public async Task Reschedule_with_null_deactivates()
    {
        using var h = NewStore();
        int id = await h.Store.CreateAsync(Daily(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));

        await h.Store.RescheduleAsync(id, null);   // caso único: cumpriu

        Assert.False((await h.Store.ListAsync()).First(s => s.Id == id).Active);
    }

    // ---------- exclusão (establishment-and-readable-dashboard, D14) ----------

    [Fact]
    public async Task Delete_removes_the_schedule_from_the_list_and_from_the_due_ones()
    {
        using var h = NewStore();
        var past = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        int id = await h.Store.CreateAsync(Daily(past));
        int other = await h.Store.CreateAsync(Daily(past));

        Assert.True(await h.Store.DeleteAsync(id));

        Assert.Equal([other], (await h.Store.ListAsync()).Select(s => s.Id));
        Assert.Equal([other], (await h.Store.ListDueAsync(past.AddDays(1))).Select(s => s.Id));   // não dispara mais
    }

    [Fact]
    public async Task Delete_by_another_tenant_is_not_found_and_keeps_the_schedule()
    {
        using var h = NewStore();
        int id = await h.Store.CreateAsync(Daily(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));

        Assert.False(await h.StoreFor("tenant-b").DeleteAsync(id));   // o id é do tenant-a (ADR-0028)

        Assert.Single(await h.Store.ListAsync());
    }

    [Fact]
    public async Task Rescheduling_a_deleted_schedule_does_nothing_and_does_not_fail()
    {
        // O agendador lista os vencidos e reprograma depois de rodar: a exclusão pode cair no meio.
        using var h = NewStore();
        int id = await h.Store.CreateAsync(Daily(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));
        await h.Store.DeleteAsync(id);

        await h.Store.RescheduleAsync(id, new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero));

        Assert.Empty(await h.Store.ListAsync());
    }

    [Fact]
    public async Task Delete_of_an_unknown_id_is_not_found()
    {
        using var h = NewStore();

        Assert.False(await h.Store.DeleteAsync(404));
    }

    [Fact]
    public async Task Executions_of_a_deleted_schedule_stay_in_the_history_with_their_own_data()
    {
        // Sem chave estrangeira: a execução guarda os próprios dados, e o ScheduleId fica solto.
        using var h = NewStore();
        int id = await h.Store.CreateAsync(Daily(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));
        var executions = new SqlExecutionStore(h.Db, TimeProvider.System);
        await executions.RecordAsync(Execution(id, "2026-07-19"));
        await executions.RecordAsync(Execution(id, "2026-07-20"));

        Assert.True(await h.Store.DeleteAsync(id));

        IReadOnlyList<ExecutionSummary> history = await new SqlExecutionQueries(h.Db, new StubTenantContext("tenant-a")).ListRecentAsync(10);
        Assert.Equal(2, history.Count);
        Assert.All(history, e =>
        {
            Assert.Equal(IntegrationMode.ScheduledDaily, e.Mode);
            Assert.Equal("12345678", e.CompanyCode);
            Assert.Equal("0001", e.BranchCode);
            Assert.Equal(3, e.DiscoveredCount);
        });
        Assert.Equal(["2026-07-20", "2026-07-19"], history.Select(e => e.PeriodStart));
    }

    private static IntegrationExecution Execution(int scheduleId, string day) => new()
    {
        Mode = IntegrationMode.ScheduledDaily,
        TenantId = "tenant-a",
        CompanyCode = "12345678",
        BranchCode = "0001",
        PeriodStart = DateTimeOffset.Parse($"{day}T00:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture),
        PeriodEnd = DateTimeOffset.Parse($"{day}T23:59:59-03:00", System.Globalization.CultureInfo.InvariantCulture),
        DiscoveredCount = 3,
        ScheduleId = scheduleId,
    };

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
        return new Harness(db, conn, new SqlScheduleStore(db, TimeProvider.System, new StubTenantContext("tenant-a")));
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlScheduleStore store) : IDisposable
    {
        public ProcessingDbContext Db => db;

        public SqlScheduleStore Store => store;

        /// <summary>O mesmo banco, visto por um usuário de outro tenant.</summary>
        public SqlScheduleStore StoreFor(string tenantId) => new(db, TimeProvider.System, new StubTenantContext(tenantId));

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
