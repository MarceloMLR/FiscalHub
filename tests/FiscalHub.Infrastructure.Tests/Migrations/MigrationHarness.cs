using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// O harness de migração (change <c>platform-account-and-connectors</c>, D12): roda as migrações EF contra o SQL Server de
/// verdade — o <c>sql</c> do <c>docker-compose.yml</c> —, num banco descartável por teste. As conversões de dado são T-SQL,
/// e o SQLite dos outros testes não as executa: só aqui uma migração é provada.
/// <para>Opt-in: só roda com <see cref="Variable"/> definida, com a connection string do servidor, sem o banco. Sem ela, os
/// testes aparecem como pulados e não tocam a rede — CI inclusive. Pulado não é prova: uma tarefa de migração só se fecha
/// com a saída de uma execução com o SQL Server no ar.</para>
/// </summary>
internal sealed class MigrationHarness : IAsyncDisposable
{
    public const string Variable = "FISCALHUB_SQL_HARNESS";

    private readonly string _server;

    private MigrationHarness(string server, string database)
    {
        _server = server;
        Database = database;
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = database }.ConnectionString;
    }

    /// <summary>O banco deste teste, criado pela primeira migração e apagado no fim.</summary>
    public string Database { get; }

    public string ConnectionString { get; }

    public static MigrationHarness Create()
    {
        string server = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{Variable} não está definida: o harness precisa do SQL Server.");

        return new MigrationHarness(server, $"FiscalHubMigrations_{Guid.NewGuid():N}");
    }

    /// <summary>O motivo de pular um teste do harness, ou <c>null</c> com a variável definida.</summary>
    public static string? SkipReason()
        => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))
            ? $"Harness de migração contra SQL Server: suba o 'sql' do docker compose e defina {Variable} "
                + "(a connection string do servidor, sem o banco)."
            : null;

    public ProcessingDbContext NewContext()
        => new(new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlServer(ConnectionString).Options);

    /// <summary>
    /// Aplica as migrações até a <paramref name="target"/>, inclusive, ou desfaz as que vierem depois dela. Sem alvo, até
    /// a última. Um <c>Down</c> que lança chega aqui como a exceção dele.
    /// </summary>
    public async Task MigrateToAsync(string? target = null)
    {
        await using ProcessingDbContext db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using SqlConnection connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>As linhas do <paramref name="sql"/>, cada uma como coluna → valor (<c>DBNull</c> vira <c>null</c>).</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(string sql)
    {
        await using SqlConnection connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>As colunas da tabela, em ordem de criação. Vazio se a tabela não existe.</summary>
    public async Task<IReadOnlyList<string>> ColumnsOfAsync(string table)
        => [.. (await QueryAsync($"SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') ORDER BY column_id"))
            .Select(r => (string)r["name"]!)];

    public async Task<IReadOnlyList<string>> AppliedMigrationsAsync()
    {
        await using ProcessingDbContext db = NewContext();
        return [.. await db.Database.GetAppliedMigrationsAsync()];
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();   // senão o DROP espera as conexões do pool
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(_server) { InitialCatalog = "master" }.ConnectionString);
        await master.OpenAsync();
        await using var command = new SqlCommand(
            $"IF DB_ID(N'{Database}') IS NOT NULL BEGIN ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}]; END",
            master);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

/// <summary>Um teste do harness de migração: pulado sem <see cref="MigrationHarness.Variable"/>.</summary>
public sealed class SqlServerHarnessFactAttribute : FactAttribute
{
    public SqlServerHarnessFactAttribute() => Skip = MigrationHarness.SkipReason();
}

/// <summary>Um teste com casos do harness de migração, um banco por caso: pulado sem <see cref="MigrationHarness.Variable"/>.</summary>
public sealed class SqlServerHarnessTheoryAttribute : TheoryAttribute
{
    public SqlServerHarnessTheoryAttribute() => Skip = MigrationHarness.SkipReason();
}
