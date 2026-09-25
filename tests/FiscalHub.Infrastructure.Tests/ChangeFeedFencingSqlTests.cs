using System.Data.Common;
using FiscalHub.Application.Coordination;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Confere o SQL do fencing (ADR-0024): o avanço da marca é UMA instrução, condicionada ao lease com
/// <c>EXISTS</c> e monotônica — em SQLite e em SQL Server. Se virar duas instruções (ler o lease e depois
/// gravar), volta a janela em que duas réplicas escrevem.
/// </summary>
public class ChangeFeedFencingSqlTests
{
    private static readonly LeaseClaim Claim = new("changefeed:Dynamics365:tenant-a", "replica-1");

    [Fact]
    public async Task Sqlite_advance_is_a_single_conditional_update()
    {
        var capture = new CommandCapture(suppress: false);
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        await using var db = new ProcessingDbContext(new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseSqlite(conn).AddInterceptors(capture).Options);
        db.Database.EnsureCreated();
        var store = new SqlChangeFeedCursorStore(db, TimeProvider.System);
        capture.Commands.Clear();

        await store.TryAdvanceWatermarkAsync("tenant-a", "Dynamics365", DateTimeOffset.UtcNow, Claim);

        AssertSingleFencedUpdate(capture.Commands);
    }

    [Fact]
    public async Task SqlServer_advance_is_a_single_conditional_update()
    {
        // Sem servidor: a conexão e a execução são suprimidas; só o texto do comando é capturado.
        var capture = new CommandCapture(suppress: true);
        await using var db = new ProcessingDbContext(new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseSqlServer("Server=nao-existe;Database=x;Trusted_Connection=true")
            .AddInterceptors(capture, new SuppressOpen()).Options);
        var store = new SqlChangeFeedCursorStore(db, TimeProvider.System);

        await store.TryAdvanceWatermarkAsync("tenant-a", "Dynamics365", DateTimeOffset.UtcNow, Claim);

        AssertSingleFencedUpdate(capture.Commands);
    }

    private static void AssertSingleFencedUpdate(List<string> commands)
    {
        string sql = Assert.Single(commands);
        Assert.StartsWith("UPDATE", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Leases", sql, StringComparison.Ordinal);
        Assert.Contains("WatermarkTicks", sql, StringComparison.Ordinal);
    }

    private sealed class CommandCapture(bool suppress) : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(suppress ? InterceptionResult<int>.SuppressWithResult(0) : result);
        }
    }

    private sealed class SuppressOpen : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
