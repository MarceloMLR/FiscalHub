using System.Text.Json.Nodes;
using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// As três migrações da change <c>platform-account-and-connectors</c> em sequência, num <c>Migrate</c> só, como a subida
/// do host as aplica (tarefa 9.4): da forma de antes da change — as duas seções de ambiente, o adapter de saída e os
/// módulos no tenant — à conta, ao conector, aos módulos e à integração de notas à espera do host; e o preenchimento da
/// primeira subida. Os testes de cada migração partem da forma da anterior; este atravessa as três de uma vez.
/// </summary>
public class MigrationSequenceTests
{
    private const string BeforeChange = "20261005191907_AddExecutionEstablishmentTaxId";
    private const string M3 = "20261014051204_AddConnectorModulesAndIntegrations";

    // As duas seções de ambiente: a do sandbox com o endpoint de token, a de produção sem ele.
    private const string Sections =
        """{"sandbox":{"baseUrl":"http://localhost:5100/","clientId":"sb-client","clientSecretRef":"kv:fh-{t}--outbound--sandbox--clientsecret","tokenUrl":"http://localhost:5100/oauth/token","establishments":{"44278225000180":{"codigoEmpresa":"001","codigoContribuinte":"001"}}},"production":{"baseUrl":"https://api.avalara.com/","clientId":"prod-client","clientSecretRef":"kv:fh-{t}--outbound--production--clientsecret","establishments":{}}}""";

    [SqlServerHarnessFact]
    public async Task From_the_shape_before_the_change_one_migrate_reaches_the_account_the_connector_and_the_waiting_invoices_path()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(BeforeChange);
        await InsertAsync(harness, "tenant-a", "Sandbox", """["Fiscal","Contabil","Inventario"]""");
        await InsertAsync(harness, "tenant-b", "Production", null);

        await harness.MigrateToAsync();   // as três de uma vez, como o Migrate da subida

        Assert.Equal(M3, (await harness.AppliedMigrationsAsync())[^1]);
        IReadOnlyList<string> profile = await harness.ColumnsOfAsync("ConnectorProfiles");
        Assert.All(new[] { "Environment", "OutboundAdapter", "OutboundSettings", "Modules" }, gone => Assert.DoesNotContain(gone, profile));

        // A conta: a credencial da seção do ambiente gravado, sem a referência do segredo (D8), e o endpoint de token só
        // onde a seção o tinha.
        Assert.Equal(
            ["tenant-a|Avalara|sb-client|null|http://localhost:5100/oauth/token", "tenant-b|Avalara|prod-client|null|null"],
            Lines(await harness.QueryAsync("SELECT TenantId, Platform, ClientId, ClientSecretRef, TokenUrl FROM PlatformAccounts ORDER BY TenantId")));

        // O conector: a base da seção vira a da v2, os establishments vão como estavam, e nada da credencial nem da outra
        // seção fica nas settings.
        JsonObject a = await SettingsOfAsync(harness, "tenant-a");
        Assert.Equal("http://localhost:5100/", (string?)a["baseUrls"]!["v2"]);
        Assert.Equal("001", (string?)a["establishments"]!["44278225000180"]!["codigoEmpresa"]);
        JsonObject b = await SettingsOfAsync(harness, "tenant-b");
        Assert.Equal("https://api.avalara.com/", (string?)b["baseUrls"]!["v2"]);
        Assert.All(new[] { a, b }, settings =>
        {
            Assert.Equal(["baseUrls", "establishments"], settings.Select(p => p.Key).Order().ToArray());
            Assert.DoesNotContain("--outbound--", settings.ToJsonString());
        });
        Assert.DoesNotContain("prod-client", a.ToJsonString());
        Assert.DoesNotContain("sb-client", b.ToJsonString());

        // Os módulos: os do tenant, com o Fiscal sempre; o nulo dá só o Fiscal.
        Assert.Equal(
            ["tenant-a|Fiscal", "tenant-a|Contabil", "tenant-a|Inventario", "tenant-b|Fiscal"],
            Lines(await harness.QueryAsync(
                """
                SELECT a.TenantId, m.Module FROM ConnectorModules m
                JOIN Connectors c ON c.Id = m.ConnectorId JOIN PlatformAccounts a ON a.Id = c.AccountId
                ORDER BY a.TenantId, CASE m.Module WHEN N'Fiscal' THEN 0 WHEN N'Contabil' THEN 1 ELSE 2 END
                """)));

        // A integração de notas, uma por tenant, sob o Fiscal, com o caminho nulo à espera do host e sem lote.
        const string Integrations =
            """
            SELECT i.TenantId, m.Module, i.[Key], i.Endpoint, i.BatchSize FROM ConnectorIntegrations i
            JOIN ConnectorModules m ON m.Id = i.ModuleId ORDER BY i.TenantId
            """;
        Assert.Equal(["tenant-a|Fiscal|notas-fiscais|null|null", "tenant-b|Fiscal|notas-fiscais|null|null"], Lines(await harness.QueryAsync(Integrations)));

        // A primeira subida preenche o caminho dos dois, uma vez (D10).
        await using ServiceProvider services = new ServiceCollection()
            .AddDbContext<ProcessingDbContext>(o => o.UseSqlServer(harness.ConnectionString)).BuildServiceProvider();
        Assert.Equal(2, await services.FillPendingIntegrationPathAsync("Avalara", ConnectorIntegrations.Invoices, "taxcompliance/v2/fiscal/dfe"));
        Assert.Equal(
            ["tenant-a|Fiscal|notas-fiscais|taxcompliance/v2/fiscal/dfe|null", "tenant-b|Fiscal|notas-fiscais|taxcompliance/v2/fiscal/dfe|null"],
            Lines(await harness.QueryAsync(Integrations)));
    }

    private static Task InsertAsync(MigrationHarness harness, string tenant, string environment, string? modules)
        => harness.ExecuteAsync(
            $"""
            INSERT INTO ConnectorProfiles (TenantId, Environment, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings, Modules)
            VALUES (N'{tenant}', N'{environment}', N'Xml', N'{"{}"}', N'Avalara', N'{Sections.Replace("{t}", tenant)}',
                    {(modules is null ? "NULL" : $"N'{modules}'")})
            """);

    private static async Task<JsonObject> SettingsOfAsync(MigrationHarness harness, string tenant)
        => JsonNode.Parse((string)Assert.Single(await harness.QueryAsync(
            $"SELECT c.Settings FROM Connectors c JOIN PlatformAccounts a ON a.Id = c.AccountId WHERE a.TenantId = N'{tenant}'"))["Settings"]!)!.AsObject();

    // Cada linha como os valores separados por "|", com o nulo escrito, para não se confundir com o texto vazio.
    private static string[] Lines(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => [.. rows.Select(r => string.Join("|", r.Values.Select(v => v?.ToString() ?? "null")))];
}
