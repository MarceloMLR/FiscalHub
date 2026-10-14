using System.Text.Json.Nodes;
using FiscalHub.Infrastructure.Migrations;

namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// Prova a M1 (change <c>platform-account-and-connectors</c>, design D13) contra o SQL Server: a seção do ambiente gravado
/// vira as settings de saída, a do outro ambiente some, a coluna <c>Environment</c> sai — e desfazer falha sem mexer no
/// banco, porque não há de onde reconstruir o que foi descartado.
/// </summary>
public class MoveEnvironmentToInstanceTests
{
    private const string Before = "20261005191907_AddExecutionEstablishmentTaxId";
    private const string M1 = "20261013160736_MoveEnvironmentToInstance";

    private const string BothSections =
        """{"sandbox":{"baseUrl":"http://localhost:5100/","clientId":"sb-client","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret","establishments":{"44278225000180":{"codigoEmpresa":"E","codigoContribuinte":"C"}},"limite":3,"ativo":true,"nada":null},"production":{"baseUrl":"https://api.avalara.com/","clientId":"prod-client","clientSecretRef":"kv:fh-tenant-a--outbound--production--clientsecret"},"extra":"fica"}""";

    [SqlServerHarnessFact]
    public async Task The_section_of_the_stored_environment_becomes_the_outbound_settings_and_the_other_is_dropped()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Before);
        await InsertAsync(harness, "tenant-a", "Sandbox", BothSections);
        await InsertAsync(harness, "tenant-b", "Production", BothSections.Replace("tenant-a", "tenant-b"));

        await harness.MigrateToAsync(M1);

        Assert.DoesNotContain("Environment", await harness.ColumnsOfAsync("ConnectorProfiles"));

        JsonObject sandbox = await OutboundOfAsync(harness, "tenant-a");
        Assert.Equal("http://localhost:5100/", (string?)sandbox["baseUrl"]);
        Assert.Equal("sb-client", (string?)sandbox["clientId"]);
        Assert.Equal("kv:fh-tenant-a--outbound--sandbox--clientsecret", (string?)sandbox["clientSecretRef"]);
        Assert.Equal("E", (string?)sandbox["establishments"]!["44278225000180"]!["codigoEmpresa"]);
        Assert.Equal(3, (int?)sandbox["limite"]);          // número continua número
        Assert.True((bool?)sandbox["ativo"]);              // booleano continua booleano
        Assert.True(sandbox.ContainsKey("nada"));          // e o nulo continua nulo
        Assert.Null(sandbox["nada"]);
        Assert.Equal("fica", (string?)sandbox["extra"]);   // a chave da raiz que não é seção fica
        Assert.False(sandbox.ContainsKey("sandbox"));
        Assert.False(sandbox.ContainsKey("production"));
        Assert.DoesNotContain("prod-client", sandbox.ToJsonString());

        JsonObject production = await OutboundOfAsync(harness, "tenant-b");
        Assert.Equal("https://api.avalara.com/", (string?)production["baseUrl"]);
        Assert.Equal("prod-client", (string?)production["clientId"]);
        Assert.Equal("fica", (string?)production["extra"]);
        Assert.DoesNotContain("sb-client", production.ToJsonString());
    }

    [SqlServerHarnessFact]
    public async Task Invalid_json_is_left_alone()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Before);
        await InsertAsync(harness, "tenant-c", "Sandbox", """{ quebrado""");

        await harness.MigrateToAsync(M1);

        Assert.Equal("""{ quebrado""", await RawOutboundOfAsync(harness, "tenant-c"));   // a leitura do adapter já o recusa
    }

    // Antes da trava, este perfil virava {"extra":"fica"}: a seção de produção sumia, e o Sandbox, que não estava lá, era
    // presumido vazio. É conversão no escuro, e a migração não tem volta.
    [SqlServerHarnessFact]
    public async Task An_environment_whose_section_is_missing_makes_the_migration_fail_and_the_database_stays_as_it_was()
        => await AssertRefusedAsync(environment: "Sandbox", """{"production":{"clientId":"prod-client"},"extra":"fica"}""");

    // ---- Os casos que destroem dado: a migração não tem volta, e o que ela erra fica errado ----

    [SqlServerHarnessFact]
    public async Task A_capitalized_section_key_is_found_and_flattened_and_the_profile_does_not_end_empty()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Before);
        await InsertAsync(harness, "tenant-a", "Sandbox",
            """{"Sandbox":{"baseUrl":"http://localhost:5100/","clientId":"sb-client"},"Production":{"clientId":"prod-client"},"extra":"fica"}""");

        await harness.MigrateToAsync(M1);

        JsonObject settings = await OutboundOfAsync(harness, "tenant-a");
        Assert.Equal("http://localhost:5100/", (string?)settings["baseUrl"]);
        Assert.Equal("sb-client", (string?)settings["clientId"]);
        Assert.Equal("fica", (string?)settings["extra"]);
        Assert.DoesNotContain("prod-client", settings.ToJsonString());
        Assert.False(settings.ContainsKey("Sandbox"));
        Assert.False(settings.ContainsKey("Production"));
    }

    // A coluna Environment é NOT NULL desde a AddConnectorProfiles: o nulo não chega ao banco. O em branco chega, e é o mesmo
    // caso — um ambiente que não nomeia seção nenhuma.
    [SqlServerHarnessFact]
    public async Task A_blank_environment_with_both_sections_makes_the_migration_fail_and_the_database_stays_as_it_was()
        => await AssertRefusedAsync(environment: "", BothSections);

    [SqlServerHarnessFact]
    public async Task An_environment_that_names_no_section_makes_the_migration_fail_and_the_database_stays_as_it_was()
        => await AssertRefusedAsync(environment: "Hml", BothSections);

    [SqlServerHarnessFact]
    public async Task An_already_flat_profile_goes_through_intact_whatever_its_environment()
    {
        // Sem seção nenhuma, o dado já está no formato novo: não é ambíguo, e a migração não pode falhar por ele.
        const string flat = """{"baseUrl":"https://avalara/","clientId":"abc","establishments":{"44278225000180":{"codigoEmpresa":"E"}},"limite":3}""";
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Before);
        await InsertAsync(harness, "tenant-a", "Hml", flat);
        await InsertAsync(harness, "tenant-b", "Production", flat);

        await harness.MigrateToAsync(M1);

        Assert.Equal(M1, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(flat), await OutboundOfAsync(harness, "tenant-a")));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(flat), await OutboundOfAsync(harness, "tenant-b")));
    }

    private static async Task AssertRefusedAsync(string environment, string outbound)
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Before);
        await InsertAsync(harness, "tenant-a", environment, outbound);
        await InsertAsync(harness, "tenant-b", "Sandbox", BothSections.Replace("tenant-a", "tenant-b"));   // uma linha boa, que também não muda

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M1));

        Assert.Contains("não nomeia nenhuma seção", ex.Message);
        Assert.Equal(Before, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Contains("Environment", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Equal(outbound, await RawOutboundOfAsync(harness, "tenant-a"));
        Assert.Equal(BothSections.Replace("tenant-a", "tenant-b"), await RawOutboundOfAsync(harness, "tenant-b"));
    }

    [SqlServerHarnessFact]
    public async Task Undoing_it_fails_with_the_message_and_leaves_the_database_as_it_was()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(Before);
        await InsertAsync(harness, "tenant-a", "Sandbox", BothSections);
        await harness.MigrateToAsync(M1);
        string settings = await RawOutboundOfAsync(harness, "tenant-a");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => harness.MigrateToAsync(Before));

        Assert.Equal(MoveEnvironmentToInstance.IrreversibleMessage, ex.Message);
        Assert.Contains("de ida só", ex.Message);
        Assert.Equal(M1, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.DoesNotContain("Environment", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Equal(settings, await RawOutboundOfAsync(harness, "tenant-a"));
    }

    private static Task InsertAsync(MigrationHarness harness, string tenant, string environment, string outbound)
        => harness.ExecuteAsync(
            $"""
            INSERT INTO ConnectorProfiles (TenantId, Environment, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings)
            VALUES (N'{tenant}', N'{environment}', N'Xml', N'{"{}"}', N'Avalara', N'{outbound.Replace("'", "''")}')
            """);

    private static async Task<string> RawOutboundOfAsync(MigrationHarness harness, string tenant)
        => (string)(await harness.QueryAsync($"SELECT OutboundSettings FROM ConnectorProfiles WHERE TenantId = N'{tenant}'"))
            .Single()["OutboundSettings"]!;

    private static async Task<JsonObject> OutboundOfAsync(MigrationHarness harness, string tenant)
        => JsonNode.Parse(await RawOutboundOfAsync(harness, tenant))!.AsObject();
}
