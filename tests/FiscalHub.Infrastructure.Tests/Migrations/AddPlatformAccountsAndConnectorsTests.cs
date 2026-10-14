using System.Text.Json.Nodes;
using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Infrastructure.Tests.Migrations;

/// <summary>
/// Prova a M2 (change <c>platform-account-and-connectors</c>, design D8 e D13) contra o SQL Server: a conta e o conector
/// nascem do perfil achatado pela M1, sem copiar a referência do segredo; a <c>baseUrl</c> vira a base da <c>v2</c>; e o
/// <c>Down</c> remonta as settings achatadas. A regra das duas travas: nada é removido que não tenha sido lido — o <c>Up</c>
/// recusa a chave que não reconheceria, e o <c>Down</c>, a forma nova que o perfil achatado não comporta.
/// </summary>
public class AddPlatformAccountsAndConnectorsTests
{
    private const string M1 = "20261013160736_MoveEnvironmentToInstance";
    private const string M2 = "20261013170710_AddPlatformAccountsAndConnectors";
    private const string BeforeChange = "20261005191907_AddExecutionEstablishmentTaxId";

    private const string Flat =
        """{"baseUrl":"https://avalara/","clientId":"abc","clientSecretRef":"kv:fh-tenant-a--outbound--clientsecret","tokenUrl":"https://login.avalara/oauth/token","establishments":{"44278225000180":{"codigoEmpresa":"E","codigoContribuinte":"C"}},"limite":3}""";

    [SqlServerHarnessFact]
    public async Task The_account_and_the_connector_are_born_from_the_flat_settings()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);

        await harness.MigrateToAsync(M2);

        Assert.DoesNotContain("OutboundAdapter", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.DoesNotContain("OutboundSettings", await harness.ColumnsOfAsync("ConnectorProfiles"));

        IReadOnlyDictionary<string, object?> account = Assert.Single(await harness.QueryAsync("SELECT * FROM PlatformAccounts"));
        Assert.Equal("tenant-a", account["TenantId"]);
        Assert.Equal("Avalara", account["Platform"]);
        Assert.Equal("abc", account["ClientId"]);
        Assert.Null(account["ClientSecretRef"]);   // a referência não é copiada (D8)
        Assert.Equal("https://login.avalara/oauth/token", account["TokenUrl"]);

        IReadOnlyDictionary<string, object?> connector = Assert.Single(await harness.QueryAsync("SELECT * FROM Connectors"));
        Assert.Equal(account["Id"], connector["AccountId"]);
        Assert.Equal("Avalara", connector["Adapter"]);
        JsonObject settings = JsonNode.Parse((string)connector["Settings"]!)!.AsObject();
        Assert.Equal("https://avalara/", (string?)settings["baseUrls"]!["v2"]);   // a v2 é rótulo da base, e a URL não muda
        Assert.Equal("E", (string?)settings["establishments"]!["44278225000180"]!["codigoEmpresa"]);
        Assert.Equal(3, (int?)settings["limite"]);
        foreach (string gone in new[] { "baseUrl", "clientId", "clientSecretRef", "tokenUrl" })
        {
            Assert.False(settings.ContainsKey(gone), $"{gone} ficou nas settings do conector");   // a credencial é da conta
        }
    }

    [SqlServerHarnessFact]
    public async Task After_it_the_secret_reads_as_not_configured_even_with_the_old_name_still_in_the_vault()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);

        await harness.MigrateToAsync(M2);
        await harness.MigrateToAsync();   // a leitura é o código de hoje, que lê a forma da última migração; a M3 não toca na conta

        // O cofre ainda tem o segredo sob o nome antigo, que continua no prefixo do tenant: se a referência tivesse sido
        // copiada, a leitura diria "configurado". A migração não recebeu cofre nenhum, e nada o tocou.
        var vault = new CountingVault { Names = { "fh-tenant-a--outbound--clientsecret" } };
        await using ProcessingDbContext db = harness.NewContext();
        var service = new ConnectorProfileService(new SqlConnectorProfileStore(db), vault, [], [], new Tenant("tenant-a"));

        ConnectorProfileView? view = await service.GetAsync();

        Assert.Equal(new SecretStatus(false, null), view!.Secrets["platform.Avalara.clientSecret"]);
        Assert.Equal(0, vault.Calls);
    }

    [SqlServerHarnessFact]
    public async Task Settings_without_a_token_url_give_an_account_without_the_endpoint()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", """{"baseUrl":"http://localhost:5100/","clientId":"mock-client"}""");

        await harness.MigrateToAsync(M2);

        IReadOnlyDictionary<string, object?> account = Assert.Single(await harness.QueryAsync("SELECT * FROM PlatformAccounts"));
        Assert.Equal("mock-client", account["ClientId"]);
        Assert.Null(account["TokenUrl"]);   // não é derivado da base: a conta fica sem o endpoint, e o envio o pede (D11)
    }

    [SqlServerHarnessFact]
    public async Task Invalid_json_goes_to_the_connector_as_it_was_and_the_account_has_no_credential()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", """{ quebrado""");

        await harness.MigrateToAsync(M2);

        IReadOnlyDictionary<string, object?> account = Assert.Single(await harness.QueryAsync("SELECT * FROM PlatformAccounts"));
        Assert.Null(account["ClientId"]);
        Assert.Equal("""{ quebrado""", Assert.Single(await harness.QueryAsync("SELECT Settings FROM Connectors"))["Settings"]);
    }

    [SqlServerHarnessFact]
    public async Task Outbound_settings_without_an_adapter_make_the_migration_fail_and_the_database_stays_as_it_was()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "", Flat);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains("sem adapter de saída", ex.Message);
        Assert.Equal(M1, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Contains("OutboundSettings", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Empty(await harness.ColumnsOfAsync("PlatformAccounts"));
        Assert.Equal(Flat, Assert.Single(await harness.QueryAsync("SELECT OutboundSettings FROM ConnectorProfiles"))["OutboundSettings"]);
    }

    [SqlServerHarnessFact]
    public async Task Undoing_it_rebuilds_the_flat_settings_from_the_account_and_the_connector()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);
        await harness.MigrateToAsync(M2);

        await harness.MigrateToAsync(M1);

        Assert.Empty(await harness.ColumnsOfAsync("PlatformAccounts"));
        Assert.Empty(await harness.ColumnsOfAsync("Connectors"));
        IReadOnlyDictionary<string, object?> row = Assert.Single(await harness.QueryAsync("SELECT OutboundAdapter, OutboundSettings FROM ConnectorProfiles"));
        Assert.Equal("Avalara", row["OutboundAdapter"]);

        // A forma de antes da M2, menos a referência do segredo, que a M2 não copiou de propósito (D8).
        JsonObject expected = JsonNode.Parse(Flat)!.AsObject();
        expected.Remove("clientSecretRef");
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse((string)row["OutboundSettings"]!)),
            $"esperado {expected.ToJsonString()}, veio {row["OutboundSettings"]}");
    }

    [SqlServerHarnessFact]
    public async Task Undoing_it_keeps_the_reference_the_account_gained_after_it()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);
        await harness.MigrateToAsync(M2);
        // O Admin digitou o segredo de novo depois da M2: a conta ganhou a referência com o nome novo.
        await harness.ExecuteAsync("UPDATE PlatformAccounts SET ClientSecretRef = N'kv:fh-tenant-a--platform--avalara--clientsecret'");

        await harness.MigrateToAsync(M1);

        JsonObject settings = JsonNode.Parse((string)Assert.Single(await harness.QueryAsync("SELECT OutboundSettings FROM ConnectorProfiles"))["OutboundSettings"]!)!.AsObject();
        Assert.Equal("kv:fh-tenant-a--platform--avalara--clientsecret", (string?)settings["clientSecretRef"]);
        Assert.Equal("https://avalara/", (string?)settings["baseUrl"]);
    }

    // ---- O Up: nada é removido que não tenha sido lido (revisão de 2026-10-13) ----

    [SqlServerHarnessTheory]
    [InlineData("BaseUrl", "https://avalara/")]
    [InlineData("ClientId", "abc")]
    [InlineData("ClientSecretRef", "kv:fh-tenant-a--outbound--clientsecret")]
    [InlineData("CLIENTSECRET", "")]   // vazio: com valor, é a trava do segredo em texto, que vem antes
    [InlineData("tokenURL", "https://login.avalara/oauth/token")]
    public async Task A_flat_key_spelled_in_another_case_makes_the_migration_fail_and_the_database_stays_as_it_was(string key, string value)
    {
        // O caminho JSON da extração distingue caixa, e a exclusão não pode distinguir menos: sem a trava, "BaseUrl" não seria
        // lida como a base e sumiria das settings do conector — ou ficaria lá, calada, e o envio falharia em produção.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        string flat = $$"""{"{{key}}":"{{value}}","establishments":{} }""";
        await InsertAsync(harness, "tenant-a", "Avalara", flat);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains("caixa diferente da esperada", ex.Message);
        await AssertStillBeforeM2Async(harness, flat);
    }

    [SqlServerHarnessTheory]
    [InlineData("clientSecret")]
    [InlineData("CLIENTSECRET")]
    public async Task A_client_secret_in_text_makes_the_migration_fail_and_the_database_stays_as_it_was(string key)
    {
        // Um segredo em texto no banco não é dado a preservar: é um segredo vazado, que precisa ser rotacionado. Apagá-lo
        // em silêncio destruiria a evidência do vazamento. A mensagem não repete o valor.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        string flat = $$"""{"baseUrl":"https://avalara/","clientId":"abc","{{key}}":"s3cr3t"}""";
        await InsertAsync(harness, "tenant-a", "Avalara", flat);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains("clientSecret em texto", ex.Message);
        Assert.Contains("rotacione o segredo na plataforma", ex.Message);
        Assert.DoesNotContain("s3cr3t", ex.Message);
        await AssertStillBeforeM2Async(harness, flat);
    }

    // ---- Cada trava do Up tem a remediação provada (D13) ----

    // Cada migração roda na própria transação: numa subida que aplica as duas, a trava da M2 desfaz só a M2, e a M1 fica
    // aplicada — o ambiente já convertido, e o host antigo sem a coluna que lê. O acerto que a trava pede é feito por SQL
    // sobre a forma da M1, e a subida seguinte aplica a M2. Uma trava nova do Up entra aqui, com o acerto dela, e no
    // pré-voo (docs/preflight/platform-account-and-connectors.sql).
    [SqlServerHarnessTheory]
    [InlineData("Avalara", """{"baseUrl":"https://avalara/","clientSecret":"s3cr3t"}""", "clientSecret em texto",
        "UPDATE ConnectorProfiles SET OutboundSettings = JSON_MODIFY(OutboundSettings, N'$.clientSecret', NULL)")]
    [InlineData("Avalara", """{"baseUrl":"https://avalara/","baseUrls":{"v1":"https://v1.avalara/"}}""", "baseUrls nas settings",
        "UPDATE ConnectorProfiles SET OutboundSettings = JSON_MODIFY(OutboundSettings, N'$.baseUrls', NULL)")]
    [InlineData("Avalara", """{"BaseUrl":"https://avalara/"}""", "caixa diferente da esperada",
        "UPDATE ConnectorProfiles SET OutboundSettings = JSON_MODIFY(JSON_MODIFY(OutboundSettings, N'$.baseUrl', JSON_VALUE(OutboundSettings, N'$.BaseUrl')), N'$.BaseUrl', NULL)")]
    [InlineData("avalara", """{"baseUrl":"https://avalara/"}""", "não é exatamente 'Avalara'",
        "UPDATE ConnectorProfiles SET OutboundAdapter = N'Avalara'")]
    [InlineData("", """{"baseUrl":"https://avalara/"}""", "sem adapter de saída",
        "UPDATE ConnectorProfiles SET OutboundAdapter = N'Avalara'")]
    public async Task Each_lock_of_the_m2_in_the_same_start_leaves_the_m1_applied_and_its_fix_goes_on_the_flat_shape(
        string adapter, string section, string reason, string fix)
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(BeforeChange);
        await harness.ExecuteAsync(
            $$"""
            INSERT INTO ConnectorProfiles (TenantId, Environment, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings)
            VALUES (N'tenant-a', N'Sandbox', N'Xml', N'{}', N'{{adapter}}', N'{"sandbox":{{section}}}')
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains(reason, ex.Message);
        Assert.Equal(M1, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.DoesNotContain("Environment", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(section),
            JsonNode.Parse((string)Assert.Single(await harness.QueryAsync("SELECT OutboundSettings FROM ConnectorProfiles"))["OutboundSettings"]!)));

        await harness.ExecuteAsync(fix);
        await harness.MigrateToAsync(M2);

        Assert.Equal(M2, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Equal("Avalara", Assert.Single(await harness.QueryAsync("SELECT Platform FROM PlatformAccounts"))["Platform"]);
        JsonNode settings = JsonNode.Parse((string)Assert.Single(await harness.QueryAsync("SELECT Settings FROM Connectors"))["Settings"]!)!;
        Assert.Equal("https://avalara/", (string?)settings["baseUrls"]!["v2"]);
    }

    [SqlServerHarnessFact]
    public async Task An_empty_client_secret_is_no_secret_and_the_migration_goes_through()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", """{"baseUrl":"https://avalara/","clientId":"abc","clientSecret":"","establishments":{}}""");

        await harness.MigrateToAsync(M2);

        JsonNode settings = JsonNode.Parse((string)Assert.Single(await harness.QueryAsync("SELECT Settings FROM Connectors"))["Settings"]!)!;
        JsonNode expected = JsonNode.Parse("""{"baseUrls":{"v2":"https://avalara/"},"establishments":{}}""")!;
        Assert.True(JsonNode.DeepEquals(expected, settings), $"esperado {expected.ToJsonString()}, veio {settings.ToJsonString()}");
        Assert.Equal("abc", Assert.Single(await harness.QueryAsync("SELECT ClientId FROM PlatformAccounts"))["ClientId"]);
    }

    [SqlServerHarnessFact]
    public async Task A_flat_base_urls_key_makes_the_migration_fail_instead_of_being_overwritten()
    {
        // A conversão escreve baseUrls: um baseUrls que já estivesse nas settings seria sobrescrito sem ter sido lido.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        const string flat = """{"baseUrl":"https://avalara/","baseUrls":{"v1":"https://v1.avalara/"}}""";
        await InsertAsync(harness, "tenant-a", "Avalara", flat);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains("baseUrls", ex.Message);
        await AssertStillBeforeM2Async(harness, flat);
    }

    [SqlServerHarnessFact]
    public async Task The_right_spelling_fills_the_v2_base_and_leaves_no_copy_of_the_key()
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara",
            """{"baseUrl":"https://avalara/","clientId":"abc","tokenUrl":"https://login.avalara/oauth/token","outro":1}""");

        await harness.MigrateToAsync(M2);

        JsonNode settings = JsonNode.Parse((string)Assert.Single(await harness.QueryAsync("SELECT Settings FROM Connectors"))["Settings"]!)!;
        JsonNode expected = JsonNode.Parse("""{"baseUrls":{"v2":"https://avalara/"},"outro":1}""")!;
        Assert.True(JsonNode.DeepEquals(expected, settings), $"esperado {expected.ToJsonString()}, veio {settings.ToJsonString()}");
        IReadOnlyDictionary<string, object?> account = Assert.Single(await harness.QueryAsync("SELECT * FROM PlatformAccounts"));
        Assert.Equal("abc", account["ClientId"]);
        Assert.Equal("https://login.avalara/oauth/token", account["TokenUrl"]);
    }

    // ---- O Down recusa o que não sabe reconstruir (revisão de 2026-10-13) ----

    [SqlServerHarnessTheory]
    [InlineData("duas contas no tenant")]
    [InlineData("dois conectores na conta")]
    [InlineData("base v1")]
    [InlineData("base de outra versão")]
    [InlineData("conector de outro adapter")]
    [InlineData("chave achatada no conector")]
    public async Task Undoing_it_refuses_what_the_flat_profile_cannot_hold_and_the_tables_stay(string situation)
    {
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);
        await harness.MigrateToAsync(M2);
        await harness.ExecuteAsync(situation switch
        {
            "duas contas no tenant" => "INSERT INTO PlatformAccounts (TenantId, Platform) VALUES (N'tenant-a', N'Sovos')",
            "dois conectores na conta" => "INSERT INTO Connectors (AccountId, Adapter, Settings) SELECT Id, N'Outro', N'{}' FROM PlatformAccounts",
            "base v1" => "UPDATE Connectors SET Settings = JSON_MODIFY(Settings, N'$.baseUrls.v1', N'https://v1.avalara/')",
            "base de outra versão" => "UPDATE Connectors SET Settings = JSON_MODIFY(Settings, N'$.baseUrls.v3', N'https://v3.avalara/')",
            // O Down tiraria o nome da plataforma: o adapter de saída achatado é um só.
            "conector de outro adapter" => "UPDATE Connectors SET Adapter = N'AvalaraAvaTax'",
            // O Down escreveria o tokenUrl da conta por cima do que o conector tem.
            _ => "UPDATE Connectors SET Settings = JSON_MODIFY(Settings, N'$.tokenUrl', N'https://outro.avalara/token')",
        });
        string before = await NewShapeAsync(harness);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M1));

        Assert.Contains("Desfazer perderia dado", ex.Message);
        Assert.Equal(M2, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.DoesNotContain("OutboundAdapter", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Equal(before, await NewShapeAsync(harness));
    }

    [SqlServerHarnessFact]
    public async Task Undoing_it_with_an_empty_base_of_another_version_goes_through()
    {
        // A base v1 vazia não é dado: a tela a grava assim quando o campo é apagado, e nada se perde ao desfazer.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);
        await harness.MigrateToAsync(M2);
        await harness.ExecuteAsync("UPDATE Connectors SET Settings = JSON_MODIFY(Settings, N'$.baseUrls.v1', N'')");

        await harness.MigrateToAsync(M1);

        JsonObject settings = JsonNode.Parse((string)Assert.Single(await harness.QueryAsync("SELECT OutboundSettings FROM ConnectorProfiles"))["OutboundSettings"]!)!.AsObject();
        Assert.Equal("https://avalara/", (string?)settings["baseUrl"]);
        Assert.False(settings.ContainsKey("baseUrls"));
    }

    // ---- A grafia do nome (revisão de 2026-10-13) ----

    [SqlServerHarnessFact]
    public async Task The_migrated_name_is_found_by_the_exact_comparisons_of_the_avalara_adapter()
    {
        // O adapter de verdade, como o host o registra: o teste de credencial e a listagem comparam o nome exato. "Avalara" é
        // a grafia que o seed, a tela e o banco de dev gravam no OutboundAdapter.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", "Avalara", Flat);
        await harness.MigrateToAsync(M2);
        await harness.MigrateToAsync();   // a leitura é o código de hoje, que lê a forma da última migração; a M3 não toca na conta
        await using ProcessingDbContext db = harness.NewContext();
        await using ServiceProvider avalara = AvalaraServices();
        TenantConnectorProfile profile = (await new SqlConnectorProfileStore(db).GetAsync("tenant-a"))!;

        // 1. A conta com teste: o serviço acha a conta pela plataforma, e a leitura do adapter acha a conta e o conector.
        CredentialTestReply reply = await CredentialTestOf(db, avalara).TestAsync("platform");

        Assert.Equal(CredentialTestStatus.Tested, reply.Status);
        Assert.Equal("Avalara", reply.Log!.Adapter);
        Assert.Contains("o Client Secret da conta de plataforma 'Avalara'", reply.Log.Detail);   // achou a conta; falta o segredo (D8)
        Assert.DoesNotContain("não tem a conta", reply.Log.Detail);

        // 2. O conector da listagem: o resolvedor só chama a listagem com o conector do adapter no tenant. Chamou, e ela
        // parou no segredo, antes de qualquer requisição; sem o conector, viria o índice sem listagem, sem chamada.
        IPlatformEstablishmentListing listing = Assert.Single(avalara.GetServices<IPlatformEstablishmentListing>());
        var resolver = new PlatformEstablishmentResolver([listing], new PlatformEstablishmentOptions(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<DispatchRejectedException>(() => resolver.GetAsync(profile, listing.Adapter));

        Assert.Contains("o Client Secret da conta de plataforma 'Avalara'", ex.Reason);
    }

    [SqlServerHarnessTheory]
    [InlineData("avalara")]   // outra caixa: só a API, sem a tela, gravaria
    [InlineData("Mock")]      // a opção que a tela antiga oferecia, e que nenhum adapter atende
    public async Task An_adapter_that_is_not_exactly_avalara_makes_the_migration_fail_and_the_database_stays_as_it_was(string adapter)
    {
        // Antes desta change o envio não lia o OutboundAdapter; depois dela, o adapter acha a conta e o conector pelo nome
        // exato. Copiado como está, o nome ficaria invisível; normalizado, a migração adivinharia o que ele queria dizer.
        await using MigrationHarness harness = MigrationHarness.Create();
        await harness.MigrateToAsync(M1);
        await InsertAsync(harness, "tenant-a", adapter, Flat);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => harness.MigrateToAsync(M2));

        Assert.Contains("não é exatamente 'Avalara'", ex.Message);
        await AssertStillBeforeM2Async(harness, Flat);
    }

    // O adapter Avalara registrado como o host o registra, com um cofre vazio: nenhuma leitura chega a fazer requisição.
    private static ServiceProvider AvalaraServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISecretStore>(new CountingVault());
        services.AddSingleton<IInstanceEnvironment>(InstanceEnvironment.From(InstanceEnvironment.Sandbox));
        services.AddAvalaraComplianceDispatcher();
        return services.BuildServiceProvider();
    }

    private static ConnectorCredentialTestService CredentialTestOf(ProcessingDbContext db, ServiceProvider avalara)
        => new(new SqlConnectorProfileStore(db), avalara.GetServices<IConnectorCredentialTest>(),
            new CredentialTestBrake(new CredentialTestOptions(), TimeProvider.System), InstanceEnvironment.From(InstanceEnvironment.Sandbox),
            new Tenant("tenant-a"));

    // A forma de antes da M2, intacta: a migração não ficou registrada, as tabelas novas não existem, e as settings são as
    // mesmas.
    private static async Task AssertStillBeforeM2Async(MigrationHarness harness, string flat)
    {
        Assert.Equal(M1, (await harness.AppliedMigrationsAsync())[^1]);
        Assert.Contains("OutboundAdapter", await harness.ColumnsOfAsync("ConnectorProfiles"));
        Assert.Empty(await harness.ColumnsOfAsync("PlatformAccounts"));
        Assert.Equal(flat, Assert.Single(await harness.QueryAsync("SELECT OutboundSettings FROM ConnectorProfiles"))["OutboundSettings"]);
    }

    // As contas e os conectores, linha a linha, para comparar antes e depois.
    private static async Task<string> NewShapeAsync(MigrationHarness harness)
    {
        IEnumerable<string> rows = (await harness.QueryAsync("SELECT * FROM PlatformAccounts ORDER BY Id"))
            .Concat(await harness.QueryAsync("SELECT * FROM Connectors ORDER BY Id"))
            .Select(r => string.Join("|", r.Select(c => $"{c.Key}={c.Value}")));
        return string.Join("\n", rows);
    }

    private static Task InsertAsync(MigrationHarness harness, string tenant, string outboundAdapter, string outbound)
        => harness.ExecuteAsync(
            $"""
            INSERT INTO ConnectorProfiles (TenantId, InboundAdapter, InboundSettings, OutboundAdapter, OutboundSettings)
            VALUES (N'{tenant}', N'Xml', N'{"{}"}', N'{outboundAdapter}', N'{outbound.Replace("'", "''")}')
            """);

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    // Conta toda chamada: a leitura de um segredo sem referência não deve nem perguntar ao cofre.
    private sealed class CountingVault : ISecretStore
    {
        public HashSet<string> Names { get; } = [];

        public int Calls { get; private set; }

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>(Names.Contains(name) ? "segredo-antigo" : null);
        }

        public Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Names.Contains(name) ? new SecretDescription(DateTimeOffset.UnixEpoch) : null);
        }
    }
}
