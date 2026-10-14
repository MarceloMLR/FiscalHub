namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// O pré-voo da change <c>platform-account-and-connectors</c> (<c>docs/preflight/platform-account-and-connectors.sql</c>,
/// design D13), rodado como o operador o roda: contra a forma de antes da change, só lendo. Cada trava da M1 e do Up da M2
/// aparece no perfil que a dispararia, e o perfil limpo não aparece. O script não decide nada — se divergir das migrações
/// para menos, a trava da migração ainda segura —, mas um script que não acha o que diz achar não serve para conferir.
/// </summary>
public class PreflightScriptTests
{
    private const string BeforeChange = "20261005191907_AddExecutionEstablishmentTaxId";

    [SqlServerHarnessFact]
    public async Task Each_lock_shows_on_the_profile_that_would_trip_it_and_a_clean_profile_does_not_show()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(BeforeChange);
        await InsertAsync(harness, "limpo", "Sandbox", "Avalara",
            """{"sandbox":{"baseUrl":"https://avalara/","clientId":"abc","clientSecret":""},"production":{"baseUrl":"https://p/"},"extra":1}""");
        await InsertAsync(harness, "ambiente", "Hml", "Avalara", """{"sandbox":{},"production":{}}""");
        await InsertAsync(harness, "sem-adapter", "Sandbox", "", """{"sandbox":{"baseUrl":"https://avalara/"}}""");
        await InsertAsync(harness, "grafia", "Sandbox", "avalara", """{"sandbox":{"baseUrl":"https://avalara/"}}""");
        await InsertAsync(harness, "segredo", "Sandbox", "Avalara", """{"sandbox":{"baseUrl":"https://avalara/","clientSecret":"s3cr3t"}}""");
        await InsertAsync(harness, "segredo-na-raiz", "Sandbox", "Avalara", """{"baseUrl":"https://avalara/","clientSecret":"s3cr3t"}""");
        await InsertAsync(harness, "caixa", "Production", "Avalara", """{"Production":{"BaseUrl":"https://avalara/"}}""");
        await InsertAsync(harness, "bases", "Sandbox", "Avalara", """{"sandbox":{"baseUrl":"https://avalara/","baseUrls":{"v1":"https://v1/"}}}""");
        await InsertAsync(harness, "modulos", "Sandbox", "Avalara", """{"sandbox":{"baseUrl":"https://avalara/"}}""", """["Fiscal","contabil"]""");
        await InsertAsync(harness, "sem-saida", "Sandbox", "", "{}", """["Fiscal","Inventario"]""");
        await InsertAsync(harness, "sem-saida-so-fiscal", "Sandbox", "", "{}", """["Fiscal"]""");

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = await harness.QueryAsync(
            File.ReadAllText(Path.Combine(RepoRoot(), "docs", "preflight", "platform-account-and-connectors.sql")));

        Assert.Equal(
            [
                "ambiente: M1: ambiente sem seção",
                "bases: M2: baseUrls nas settings",
                "caixa: M2: chave em outra caixa",
                "grafia: M2: adapter fora da grafia",
                "modulos: M3: lista de módulos ilegível",
                "segredo-na-raiz: M2: clientSecret em texto",
                "segredo: M2: clientSecret em texto",
                "sem-adapter: M2: settings sem adapter",
                "sem-saida: M3: módulos sem conector",
            ],
            rows.Select(r => $"{r["TenantId"]}: {r["Trava"]}").Order(StringComparer.Ordinal));
        Assert.All(rows, r => Assert.Equal(["Trava", "TenantId"], r.Keys));   // nenhum valor das settings sai
    }

    [SqlServerHarnessFact]
    public async Task It_only_reads()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(BeforeChange);
        await InsertAsync(harness, "segredo", "Sandbox", "Avalara", """{"sandbox":{"clientSecret":"s3cr3t"}}""");

        await harness.QueryAsync(File.ReadAllText(Path.Combine(RepoRoot(), "docs", "preflight", "platform-account-and-connectors.sql")));

        Assert.Equal(BeforeChange, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Equal("""{"sandbox":{"clientSecret":"s3cr3t"}}""",
            Assert.Single(await harness.QueryAsync("SELECT OutboundSettings FROM ConnectorProfiles"))["OutboundSettings"]);
    }

    [SqlServerHarnessFact]
    public async Task Before_undoing_the_m3_the_script_lists_every_integration_with_its_path_and_only_reads()
    {
        // O Down da M3 perde o caminho das integrações (D13): o script o guarda antes, e não muda nada.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(BeforeChange);
        await InsertAsync(harness, "tenant-a", "Sandbox", "Avalara", """{"sandbox":{"baseUrl":"https://avalara/"}}""", """["Fiscal"]""");
        await InsertAsync(harness, "tenant-b", "Sandbox", "Avalara", """{"sandbox":{"baseUrl":"https://avalara/"}}""");
        await harness.MigrateToAsync(M3);
        await harness.ExecuteAsync(
            """
            UPDATE i SET Endpoint = N'caminho/editado/pelo/admin'
            FROM ConnectorIntegrations AS i
            JOIN ConnectorModules AS m ON m.Id = i.ModuleId JOIN Connectors AS c ON c.Id = m.ConnectorId
            JOIN PlatformAccounts AS a ON a.Id = c.AccountId
            WHERE a.TenantId = N'tenant-a'
            """);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = await harness.QueryAsync(
            File.ReadAllText(Path.Combine(RepoRoot(), "docs", "preflight", "platform-account-and-connectors-before-undoing-m3.sql")));

        Assert.Equal(
            ["tenant-a notas-fiscais caminho/editado/pelo/admin", "tenant-b notas-fiscais (nulo)"],
            rows.Select(r => $"{r["TenantId"]} {r["Key"]} {r["Endpoint"] ?? "(nulo)"}"));
        Assert.Equal(M3, (await harness.AppliedMigrationsAsync())[^1]);
    }

    private const string M3 = "20261014051204_AddConnectorModulesAndIntegrations";

    private static Task InsertAsync(
        MigrationHarness harness, string tenant, string environment, string outboundAdapter, string outbound, string? modules = null)
        => harness.ExecuteAsync(
            $"""
            INSERT INTO ConnectorProfiles (TenantId, Environment, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings, Modules)
            VALUES (N'{tenant}', N'{environment}', N'Xml', N'{"{}"}', N'{outboundAdapter}', N'{outbound.Replace("'", "''")}',
                    {(modules is null ? "NULL" : $"N'{modules}'")})
            """);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FiscalHub.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
