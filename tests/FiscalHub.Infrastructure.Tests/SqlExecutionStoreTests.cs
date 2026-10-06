using FiscalHub.Application.Integrations;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o store/queries de execuções: grava e lista as execuções, mais recentes primeiro, com o CNPJ do estabelecimento
/// que a descoberta resolveu, anulável e só na execução.
/// </summary>
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

    // ---------- o CNPJ do estabelecimento resolvido (change explicit-credential-and-execution-cnpj, D6) ----------

    [Fact]
    public async Task The_establishment_cnpj_is_recorded_and_listed_back()
    {
        using var h = NewStore();

        await h.Store.RecordAsync(Exec(IntegrationMode.Manual, 2) with
        {
            CompanyCode = "44278225000180", BranchCode = "SP-01", EstablishmentTaxId = "44278225000260",
        });

        ExecutionSummary only = Assert.Single(await h.Queries.ListRecentAsync(10));
        Assert.Equal("44278225000180", only.CompanyCode);
        Assert.Equal("SP-01", only.BranchCode);
        Assert.Equal("44278225000260", only.EstablishmentTaxId);
    }

    [Fact]
    public async Task Without_the_establishment_cnpj_it_is_listed_null()
    {
        using var h = NewStore();

        await h.Store.RecordAsync(Exec(IntegrationMode.ScheduledDaily, 5) with { CompanyCode = "44278225000180" });

        Assert.Null(Assert.Single(await h.Queries.ListRecentAsync(10)).EstablishmentTaxId);
    }

    [Fact]
    public async Task A_row_written_without_the_column_is_listed_null()
    {
        // A linha gravada antes da migração: o INSERT não conhece a coluna, como a execução 16 do banco de dev.
        using var h = NewStore();
        await h.Db.Database.ExecuteSqlRawAsync(
            "INSERT INTO IntegrationExecutions (Mode, TenantId, CompanyCode, BranchCode, PeriodStart, PeriodEnd, DiscoveredCount, CreatedAt) "
            + "VALUES ('Manual', 'tenant-a', '44278225000180', 'SP-01', '2026-08-07', '2026-08-07', 0, '2026-10-05 12:00:00+00:00')");

        ExecutionSummary only = Assert.Single(await h.Queries.ListRecentAsync(10));
        Assert.Equal("SP-01", only.BranchCode);
        Assert.Null(only.EstablishmentTaxId);
    }

    // ---------- o modelo (D6) ----------

    [Fact]
    public void The_model_has_no_change_pending_against_the_sql_server_migrations()
    {
        // Sem conexão: compara o modelo com o snapshot das migrações. Uma migração esquecida apareceria só na subida do host.
        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseSqlServer("Server=localhost;Database=fiscalhub-model-check;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new ProcessingDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Only_the_execution_has_the_establishment_cnpj()
    {
        using var h = NewStore();

        IProperty column = h.Db.Model.FindEntityType(typeof(IntegrationExecutionRow))!.FindProperty("EstablishmentTaxId")!;
        Assert.True(column.IsNullable);
        Assert.Equal(20, column.GetMaxLength());

        // O agendamento registra o critério, e não o fato: ele não executou.
        Assert.Null(h.Db.Model.FindEntityType(typeof(ScheduledIntegrationRow))!.FindProperty("EstablishmentTaxId"));
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
        return new Harness(db, conn, new SqlExecutionStore(db, TimeProvider.System), new SqlExecutionQueries(db, new StubTenantContext("tenant-a")));
    }

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlExecutionStore store, SqlExecutionQueries queries) : IDisposable
    {
        public ProcessingDbContext Db => db;

        public SqlExecutionStore Store => store;
        public SqlExecutionQueries Queries => queries;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
