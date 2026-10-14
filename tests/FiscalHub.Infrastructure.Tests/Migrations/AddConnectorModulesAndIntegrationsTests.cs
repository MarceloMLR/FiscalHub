using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// Prova a M3 (change <c>platform-account-and-connectors</c>, design D3, D10 e D13) contra o SQL Server: o conector do
/// tenant ganha o módulo Fiscal, sempre, com a integração de notas e o caminho nulo, à espera do host; os módulos que
/// estavam no tenant vão para o conector, sem integrações; a coluna <c>Modules</c> sai. O <c>Down</c> devolve a lista ao
/// tenant. As travas seguem a regra da M2: nada é removido que não tenha sido lido, e nada é adivinhado — e cada trava do
/// <c>Up</c> tem a remediação provada.
/// </summary>
public class AddConnectorModulesAndIntegrationsTests
{
    private const string M1 = "20261013160736_MoveEnvironmentToInstance";
    private const string M2 = "20261013170710_AddPlatformAccountsAndConnectors";
    private const string M3 = "20261014051204_AddConnectorModulesAndIntegrations";

    private const string Flat = """{"baseUrl":"https://avalara/","clientId":"abc"}""";

    [SqlServerHarnessFact]
    public async Task The_tenant_modules_go_to_the_connector_and_fiscal_comes_with_the_invoices_integration_waiting_for_the_host()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", """["Fiscal","Contabil"]""");
        await harness.MigrateToAsync(M2);

        await harness.MigrateToAsync(M3);

        Assert.DoesNotContain("Modules", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Equal(["Fiscal", "Contabil"], await ModulesAsync(harness, "tenant-a"));
        IReadOnlyDictionary<string, object?> invoices = Assert.Single(await harness.QueryAsync(
            "SELECT i.TenantId, i.[Key], i.Endpoint, i.BatchSize, m.Module FROM ConnectorIntegrations i JOIN ConnectorModules m ON m.Id = i.ModuleId"));
        Assert.Equal(("tenant-a", "notas-fiscais", "Fiscal"), ((string)invoices["TenantId"]!, (string)invoices["Key"]!, (string)invoices["Module"]!));
        Assert.Null(invoices["Endpoint"]);    // à espera do host (D10), e não vazio, que é a inativa da tela (D5)
        Assert.Null(invoices["BatchSize"]);
    }

    [SqlServerHarnessTheory]
    [InlineData("""["Inventario"]""", new[] { "Fiscal", "Inventario" })]
    [InlineData("""["Contabil"]""", new[] { "Fiscal", "Contabil" })]
    [InlineData(null, new[] { "Fiscal" })]   // antes, o padrão
    public async Task A_tenant_without_fiscal_in_the_list_gets_it_and_its_info_becomes_fiscal_plus_the_rest(string? modules, string[] info)
    {
        // A migração decide, e não adivinha: sem o Fiscal não nasceria a integração de notas, e o envio pararia. O /info do
        // tenant muda, e o D13 registra isso.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", modules);
        await harness.MigrateToAsync(M2);

        await harness.MigrateToAsync(M3);

        await using ProcessingDbContext db = harness.NewContext();
        Assert.Equal(info, TenantModules.Of(await new SqlConnectorProfileStore(db).GetAsync("tenant-a")));
        Assert.Equal(info, await ModulesAsync(harness, "tenant-a"));
        Assert.Single(await harness.QueryAsync("SELECT * FROM ConnectorIntegrations WHERE [Key] = N'notas-fiscais'"));
    }

    [SqlServerHarnessTheory]
    [InlineData("""["Fiscal"]""")]
    [InlineData(null)]
    public async Task A_tenant_without_a_connector_and_only_fiscal_migrates_without_a_lock_and_without_modules(string? modules)
    {
        // Sem adapter de saída, a M2 não criou conector, e a M3 não tem onde ligar módulo: o tenant atravessa sem linha
        // nenhuma, e o /info responde o padrão, só o Fiscal (D7).
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", modules, outboundAdapter: "", outbound: "{}");
        await harness.MigrateToAsync(M2);

        await harness.MigrateToAsync(M3);

        Assert.Equal(M3, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Empty(await harness.QueryAsync("SELECT * FROM ConnectorModules"));
        await using ProcessingDbContext db = harness.NewContext();
        Assert.Equal(["Fiscal"], TenantModules.Of(await new SqlConnectorProfileStore(db).GetAsync("tenant-a")));
    }

    [SqlServerHarnessFact]
    public async Task On_the_first_start_after_it_the_host_fills_the_waiting_path_once()
    {
        // O preenchimento da subida (D10, 6.10) contra o SQL Server de verdade, sobre a marca que a M3 deixa.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", """["Fiscal"]""");
        await harness.MigrateToAsync(M3);
        await using ServiceProvider services = new ServiceCollection()
            .AddDbContext<ProcessingDbContext>(o => o.UseSqlServer(harness.ConnectionString)).BuildServiceProvider();

        int first = await services.FillPendingIntegrationPathAsync("Avalara", ConnectorIntegrations.Invoices, "taxcompliance/v2/fiscal/dfe");
        int second = await services.FillPendingIntegrationPathAsync("Avalara", ConnectorIntegrations.Invoices, "documents");

        Assert.Equal((1, 0), (first, second));
        Assert.Equal("taxcompliance/v2/fiscal/dfe", Assert.Single(await harness.QueryAsync("SELECT Endpoint FROM ConnectorIntegrations"))["Endpoint"]);
    }

    // ---- O Down ----

    [SqlServerHarnessTheory]
    [InlineData("""["Fiscal","Contabil","Inventario"]""", """["Fiscal","Contabil","Inventario"]""")]
    [InlineData("""["Fiscal","Contabil"]""", """["Fiscal","Contabil"]""")]
    [InlineData("""["Inventario"]""", """["Fiscal","Inventario"]""")]   // o Fiscal que a M3 ligou volta junto
    [InlineData(null, """["Fiscal"]""")]                                // o mesmo que o padrão do nulo
    public async Task Undoing_it_gives_the_tenant_back_the_modules_of_the_connector(string? before, string after)
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", before);
        await harness.MigrateToAsync(M3);

        await harness.MigrateToAsync(M2);

        Assert.Empty(await harness.ColumnsOfAsync("ConnectorModules"));
        Assert.Empty(await harness.ColumnsOfAsync("ConnectorIntegrations"));
        Assert.Equal(after, Assert.Single(await harness.QueryAsync("SELECT Modules FROM ConnectorProfiles"))["Modules"]);
    }

    [SqlServerHarnessFact]
    public async Task Undoing_it_after_the_host_filled_the_invoices_path_goes_through()
    {
        // O caminho da integração de notas não volta, e não precisa: antes da M3 o envio lê o Avalara:DocumentsPath (D13).
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", """["Fiscal"]""");
        await harness.MigrateToAsync(M3);
        await harness.ExecuteAsync("UPDATE ConnectorIntegrations SET Endpoint = N'taxcompliance/v2/fiscal/dfe'");

        await harness.MigrateToAsync(M2);

        Assert.Equal("""["Fiscal"]""", Assert.Single(await harness.QueryAsync("SELECT Modules FROM ConnectorProfiles"))["Modules"]);
    }

    [SqlServerHarnessTheory]
    [InlineData("integração além da de notas")]
    [InlineData("lote gravado")]
    [InlineData("dois conectores no tenant")]
    public async Task Undoing_it_refuses_what_the_tenant_list_cannot_hold_and_the_tables_stay(string situation)
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", """["Fiscal","Contabil"]""");
        await harness.MigrateToAsync(M3);
        await harness.ExecuteAsync(situation switch
        {
            "integração além da de notas" =>
                "INSERT INTO ConnectorIntegrations (ModuleId, TenantId, [Key], Endpoint) SELECT Id, N'tenant-a', N'plano-de-contas', N'' FROM ConnectorModules WHERE Module = N'Contabil'",
            "lote gravado" => "UPDATE ConnectorIntegrations SET BatchSize = 10",
            _ => "INSERT INTO Connectors (AccountId, Adapter, Settings) SELECT Id, N'Outro', N'{}' FROM PlatformAccounts",
        });
        string before = await NewShapeAsync(harness);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains("Desfazer perderia dado", ex.Message);
        Assert.Equal(M3, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.DoesNotContain("Modules", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Equal(before, await NewShapeAsync(harness));
    }

    // ---- O Up: nada é removido que não tenha sido lido, e nada é adivinhado; cada trava com a remediação provada ----

    // Cada migração roda na própria transação (D13): a trava da M3 desfaz só a M3, e o banco fica na forma da M2. O acerto
    // que ela pede é feito por SQL sobre essa forma, e a subida seguinte aplica a M3. Uma trava nova entra aqui, com o
    // acerto dela, e no pré-voo (docs/preflight/platform-account-and-connectors.sql).
    [SqlServerHarnessTheory]
    [InlineData("""["Fiscal","Folha"]""", "Avalara", "", "lista de módulos",
        "UPDATE ConnectorProfiles SET Modules = N'[\"Fiscal\"]'")]
    [InlineData("""["fiscal"]""", "Avalara", "", "lista de módulos",
        "UPDATE ConnectorProfiles SET Modules = N'[\"Fiscal\"]'")]
    [InlineData("não é json", "Avalara", "", "lista de módulos",
        "UPDATE ConnectorProfiles SET Modules = NULL")]
    [InlineData("""["Fiscal","Contabil"]""", "", "", "sem conector",
        "UPDATE ConnectorProfiles SET Modules = NULL")]
    [InlineData("""["Fiscal"]""", "Avalara", "INSERT INTO Connectors (AccountId, Adapter, Settings) SELECT Id, N'Outro', N'{}' FROM PlatformAccounts",
        "mais de um conector", "DELETE FROM Connectors WHERE Adapter = N'Outro'")]
    [InlineData("""["Fiscal"]""", "Avalara", "UPDATE Connectors SET Adapter = N'avalara'",
        "não é exatamente 'Avalara'", "UPDATE Connectors SET Adapter = N'Avalara'")]
    public async Task Each_lock_of_the_m3_leaves_the_m2_applied_and_its_fix_goes_on_that_shape(
        string? modules, string outboundAdapter, string atM2, string reason, string fix)
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", modules, outboundAdapter, outboundAdapter.Length == 0 ? "{}" : Flat);
        await harness.MigrateToAsync(M2);
        if (atM2.Length > 0)
        {
            await harness.ExecuteAsync(atM2);
        }

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M3));

        Assert.Contains(reason, ex.Message);
        Assert.Equal(M2, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Contains("Modules", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Empty(await harness.ColumnsOfAsync("ConnectorModules"));

        await harness.ExecuteAsync(fix);
        await harness.MigrateToAsync(M3);

        Assert.Equal(M3, (await harness.AppliedMigrationsAsync())[^1]);
    }

    private static Task InsertAsync(
        MigrationHarness harness, string tenant, string? modules, string outboundAdapter = "Avalara", string outbound = Flat)
        => harness.ExecuteAsync(
            $"""
            INSERT INTO ConnectorProfiles (TenantId, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings, Modules)
            VALUES (N'{tenant}', N'Xml', N'{"{}"}', N'{outboundAdapter}', N'{outbound}', {(modules is null ? "NULL" : $"N'{modules.Replace("'", "''")}'")})
            """);

    // Os módulos ligados no conector do tenant, na ordem da barra lateral.
    private static async Task<string[]> ModulesAsync(MigrationHarness harness, string tenant)
        => [.. (await harness.QueryAsync(
            $"""
            SELECT m.Module FROM ConnectorModules m
            JOIN Connectors c ON c.Id = m.ConnectorId JOIN PlatformAccounts a ON a.Id = c.AccountId
            WHERE a.TenantId = N'{tenant}'
            ORDER BY CASE m.Module WHEN N'Fiscal' THEN 0 WHEN N'Contabil' THEN 1 ELSE 2 END
            """)).Select(r => (string)r["Module"]!)];

    // Os conectores, os módulos e as integrações, linha a linha, para comparar antes e depois.
    private static async Task<string> NewShapeAsync(MigrationHarness harness)
    {
        IEnumerable<string> rows = (await harness.QueryAsync("SELECT * FROM Connectors ORDER BY Id"))
            .Concat(await harness.QueryAsync("SELECT * FROM ConnectorModules ORDER BY Id"))
            .Concat(await harness.QueryAsync("SELECT * FROM ConnectorIntegrations ORDER BY Id"))
            .Select(r => string.Join("|", r.Select(c => $"{c.Key}={c.Value}")));
        return string.Join("\n", rows);
    }
}
