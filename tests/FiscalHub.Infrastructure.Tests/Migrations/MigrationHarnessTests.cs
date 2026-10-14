using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// Prova o próprio harness (change <c>platform-account-and-connectors</c>, D12) com as migrações que já existiam: elas
/// sobem num banco vazio sem mudança de modelo pendente, e uma linha gravada na forma de uma migração atravessa as
/// seguintes e volta com o <c>Down</c>.
/// </summary>
public class MigrationHarnessTests
{
    // A última migração antes de os módulos irem para o perfil, a dos módulos, e a última antes da change
    // platform-account-and-connectors — a ida e volta não atravessa a M1, que é de ida só.
    private const string BeforeModules = "20260928031144_RenameRealTimeTrigger";
    private const string Modules = "20260929190220_AddConnectorProfileModules";
    private const string BeforeAccounts = "20261005191907_AddExecutionEstablishmentTaxId";

    [SqlServerHarnessFact]
    public async Task Every_current_migration_applies_on_an_empty_database_with_no_model_change_pending()
    {
        await using MigrationHarness harness = MigrationHarness.Create();

        await harness.MigrateToAsync();

        await using ProcessingDbContext db = harness.NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await harness.AppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [SqlServerHarnessFact]
    public async Task A_row_written_at_a_migration_goes_through_the_rest_and_the_down_takes_it_back()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Modules);
        await harness.ExecuteAsync(
            """
            INSERT INTO ConnectorProfiles (TenantId, Environment, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings, Modules)
            VALUES (N'tenant-a', N'Sandbox', N'Xml', N'{}', N'Avalara', N'{}', N'["Fiscal","Inventario"]')
            """);

        await harness.MigrateToAsync(BeforeAccounts);
        IReadOnlyDictionary<string, object?> after = Assert.Single(await harness.QueryAsync("SELECT TenantId, Modules FROM ConnectorProfiles"));
        Assert.Equal("""["Fiscal","Inventario"]""", after["Modules"]);

        await harness.MigrateToAsync(BeforeModules);
        Assert.DoesNotContain("Modules", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Equal("tenant-a", Assert.Single(await harness.QueryAsync("SELECT TenantId FROM ConnectorProfiles"))["TenantId"]);
        Assert.Equal(BeforeModules, (await harness.AppliedMigrationsAsync())[^1]);
    }
}
