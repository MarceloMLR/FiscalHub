using System.Text.Json;
using System.Text.Json.Nodes;
using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a gravação e a leitura do perfil de conector (ADR-0027): o segredo entra pela tela como campo de
/// escrita, vai para o cofre sob o nome que o servidor deriva, e o perfil guarda só a referência. A leitura nunca
/// devolve o valor nem a referência — só "configurado" e a data.
/// </summary>
public class ConnectorProfileServiceTests
{
    private const string Secret = "s3cr3t";
    // O segredo genérico das settings de um conector: o clientSecret ali é a credencial da plataforma, que é da conta, e é
    // recusado (change platform-account-and-connectors, grupo 4). A mecânica de segredo se prova com outro campo.
    private const string ConnectorSecretName = "fh-tenant-a--connector--avalara--apikey";
    private static readonly DateTimeOffset SavedOn = new(2026, 9, 27, 14, 30, 0, TimeSpan.Zero);

    // ---- Gravação ----

    [Fact]
    public async Task Write_field_goes_to_the_vault_and_the_profile_keeps_only_the_reference()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: $$$$"""{"usuario":"abc","apiKey":"{{{{Secret}}}}"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(Secret, h.Secrets.Values[ConnectorSecretName]);
        JsonObject outbound = Root(ConnectorSettings(h.Profiles.Stored!));
        Assert.Equal($"kv:{ConnectorSecretName}", (string?)outbound["apiKeyRef"]);
        Assert.Equal("abc", (string?)outbound["usuario"]);
        Assert.False(outbound.ContainsKey("apiKey"));
        Assert.DoesNotContain(Secret, ConnectorSettings(h.Profiles.Stored!));
        Assert.Equal("tenant-a", h.Profiles.Stored!.TenantId);   // o tenant é o do login, e não vem do corpo
    }

    // ---- O ambiente é da instância (change platform-account-and-connectors) ----

    [Fact]
    public async Task The_platform_secret_name_has_no_environment_segment()
    {
        var h = new Harness();

        await h.Service.SaveAsync(WithAccount(clientSecret: Secret));

        Assert.Equal(Secret, h.Secrets.Values["fh-tenant-a--platform--avalara--clientsecret"]);
        Assert.Equal("kv:fh-tenant-a--platform--avalara--clientsecret", Assert.Single(h.Profiles.Stored!.Accounts).ClientSecretRef);
    }

    [Fact]
    public async Task A_sandbox_and_a_production_instance_derive_the_same_name()
    {
        // Duas instâncias do mesmo tenant, cada uma com o próprio cofre: a mesma gravação dá o mesmo nome nas duas, e nada
        // do ambiente entra nele.
        var sandbox = new Harness();
        var production = new Harness();
        await sandbox.Service.SaveAsync(WithAccount(clientSecret: Secret, connectorSettings: $$$$"""{"apiKey":"{{{{Secret}}}}"}"""));
        await production.Service.SaveAsync(WithAccount(clientSecret: Secret, connectorSettings: $$$$"""{"apiKey":"{{{{Secret}}}}"}"""));

        Assert.Equal(sandbox.Secrets.Values.Keys, production.Secrets.Values.Keys);
        Assert.DoesNotContain(sandbox.Secrets.Values.Keys, k => k.Contains("sandbox") || k.Contains("production"));
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData("production")]
    [InlineData("Sandbox")]
    public async Task Connector_settings_with_an_environment_section_are_refused(string section)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: $$$$"""{"{{{{section}}}}":{"usuario":"abc","apiKey":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains($"Accounts[Avalara].Connectors[Avalara].Settings.{section}", result.Message);
        Assert.Contains("o ambiente é da instância", result.Message);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Null(h.Profiles.Stored);
    }

    [Theory]
    [InlineData("clientSecret", "clientsecret")]
    [InlineData("secret", "secret")]
    [InlineData("password", "password")]
    [InlineData("senha", "senha")]
    [InlineData("apiKey", "apikey")]
    [InlineData("token", "token")]
    [InlineData("accessToken", "accesstoken")]
    [InlineData("CLIENT_SECRET", "clientsecret")]
    [InlineData("api-key", "apikey")]
    [InlineData("Access_Token", "accesstoken")]
    public async Task Names_of_the_list_are_write_fields_at_any_level_in_the_three_settings(string field, string segment)
    {
        // As settings que são blob: a entrada, o conector e os chamados. A conta não tem blob: o segredo dela é campo tipado.
        // No conector, o clientSecret é a credencial da plataforma, recusada por outra regra (o teste dela está abaixo).
        foreach (ConnectorSettingsKind kind in new[] { ConnectorSettingsKind.Inbound, ConnectorSettingsKind.Connector, ConnectorSettingsKind.Support })
        {
            if (kind == ConnectorSettingsKind.Connector && segment == "clientsecret")
            {
                continue;
            }

            var h = new Harness();
            string json = $$$$"""{"nivel":{"fundo":{"{{{{field}}}}":"{{{{Secret}}}}"}}}""";

            ConnectorProfileSaveResult result = await h.Service.SaveAsync(RequestWith(kind, json));

            Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
            string scope = kind == ConnectorSettingsKind.Connector ? "connector--avalara" : kind.ToString().ToLowerInvariant();
            string name = $"fh-tenant-a--{scope}--nivel--fundo--{segment}";
            Assert.Equal(Secret, h.Secrets.Values[name]);
            string persisted = SettingsOf(h.Profiles.Stored!, kind);
            JsonObject fundo = (JsonObject)Section(persisted, "nivel")["fundo"]!;
            Assert.Equal($"kv:{name}", (string?)fundo[field + "Ref"]);
            Assert.False(fundo.ContainsKey(field));
            Assert.DoesNotContain(Secret, persisted);
        }
    }

    [Theory]
    [InlineData("""{"usuario":"xyz"}""")]
    [InlineData("""{"usuario":"xyz","apiKey":""}""")]
    [InlineData("""{"usuario":"xyz","apiKey":null}""")]
    public async Task Absent_write_field_keeps_the_stored_reference_without_touching_the_vault(string outbound)
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"usuario":"abc","apiKeyRef":"kv:{{{{ConnectorSecretName}}}}"}"""));

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: outbound));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(0, h.Secrets.SetCount);
        JsonObject saved = Root(ConnectorSettings(h.Profiles.Stored!));
        Assert.Equal($"kv:{ConnectorSecretName}", (string?)saved["apiKeyRef"]);
        Assert.Equal("xyz", (string?)saved["usuario"]);
        Assert.False(saved.ContainsKey("apiKey"));
    }

    // A máscara da tela é placeholder, e nunca valor. Se um cliente a mandar como valor, a gravação falha alto, e o
    // segredo gravado não é destruído em silêncio (change module-navigation-and-integration-panel, D3).
    [Theory]
    [InlineData("••••••••")]
    [InlineData("********")]
    [InlineData("* * * *")]
    [InlineData("●●●●")]
    [InlineData("∗∗∗")]
    public async Task Mask_as_the_value_of_a_write_field_is_refused_without_any_write(string mask)
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"usuario":"abc","apiKeyRef":"kv:{{{{ConnectorSecretName}}}}"}"""));
        h.Secrets.Values[ConnectorSecretName] = Secret;
        TenantConnectorProfile before = h.Profiles.Stored!;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: "{\"usuario\":\"abc\",\"apiKey\":\"" + mask + "\"}"));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.apiKey", result.Message);
        Assert.Contains("máscara", result.Message);
        Assert.DoesNotContain(mask, result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.Equal(Secret, h.Secrets.Values[ConnectorSecretName]);   // o segredo gravado continua o mesmo
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
        Assert.Empty(h.Observer.Tenants);
    }

    [Fact]
    public async Task Mask_in_the_inbound_settings_is_refused_too()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: """{"auth":{"clientSecret":"********"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("InboundSettings.auth.clientSecret", result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
    }

    [Fact]
    public async Task Secret_with_a_mask_character_among_others_is_accepted()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: """{"apiKey":"ab*cd"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal("ab*cd", h.Secrets.Values[ConnectorSecretName]);
    }

    [Fact]
    public async Task Stored_reference_is_not_carried_to_another_adapter()
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"apiKeyRef":"kv:{{{{ConnectorSecretName}}}}"}"""));

        await h.Service.SaveAsync(Request(outbound: """{"baseUrls":{"v2":"http://localhost:5100/"}}""", outboundAdapter: "Outra", connectorAdapter: "Outro"));

        Assert.False(Root(ConnectorSettings(h.Profiles.Stored!)).ContainsKey("apiKeyRef"));
    }

    [Theory]
    [InlineData("""{"apiKeyRef":"kv:fh-tenant-a--connector--avalara--apikey"}""")]   // a de outro tenant
    [InlineData("""{"apiKeyRef":"kv:fh-tenant-b--connector--avalara--apikey"}""")]   // até a do próprio
    [InlineData("""{"API_KEY_REF":"s3cr3t"}""")]
    public async Task Reference_in_the_request_is_refused_without_any_write(string outbound)
    {
        var h = new Harness(tenant: "tenant-b", stored: Profile(tenant: "tenant-b"));
        TenantConnectorProfile before = h.Profiles.Stored!;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: outbound, inbound: $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.", result.Message);
        Assert.Contains("referência", result.Message);
        Assert.Equal(0, h.Secrets.SetCount);   // nem o segredo válido da entrada vai para o cofre
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
        Assert.Empty(h.Observer.Tenants);
        Assert.DoesNotContain(Secret, result.Message);
    }

    [Theory]
    [InlineData("""{"apiKey":"s3cr3t" """)]
    [InlineData("""["s3cr3t"]""")]
    public async Task Invalid_json_is_refused_without_any_write(string outbound)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: outbound, inbound: $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings", result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Empty(h.Observer.Tenants);
        Assert.DoesNotContain(Secret, result.Message);
    }

    // ---- Seção poll: a guarda julga o que a gravação escreve (design D8) ----

    private const string InboundSecretName = "fh-tenant-a--inbound--auth--clientsecret";

    [Theory]
    [InlineData("""{"poll":{"enabled":"sim","overlapSeconds":300}}""", "InboundSettings.poll.enabled", "verdadeiro ou falso", "veio texto")]
    [InlineData("""{"poll":{"enabled":false,"overlapSeconds":0}}""", "InboundSettings.poll.overlapSeconds", "pelo menos 1", "veio 0")]
    [InlineData("""{"poll":"ligado"}""", "InboundSettings.poll", "um objeto", "veio texto")]
    public async Task Poll_value_being_written_that_the_collector_cannot_read_is_refused_without_any_write(
        string inbound, string field, string acceptedForm, string received)
    {
        var h = new Harness(stored: Profile(inbound: """{"poll":{"enabled":false,"overlapSeconds":300}}"""));
        TenantConnectorProfile before = h.Profiles.Stored!;
        JsonObject body = JsonNode.Parse(inbound)!.AsObject();
        body["auth"] = new JsonObject { ["clientSecret"] = Secret };   // nem o segredo válido vai para o cofre

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: body.ToJsonString()));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains(field, result.Message);
        Assert.Contains(acceptedForm, result.Message);
        Assert.Contains(received, result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
        Assert.Empty(h.Observer.Tenants);
    }

    [Fact]
    public async Task Invalid_poll_value_already_stored_does_not_lock_the_screen()
    {
        // Posto por SQL: a tela não edita o overlapSeconds, e o devolve igual em todo PUT.
        var h = new Harness(stored: Profile(inbound: """{"url":"https://erp/","poll":{"enabled":false,"overlapSeconds":0}}"""));

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(
            inbound: $$$$"""{"url":"https://erp/","auth":{"clientSecret":"{{{{Secret}}}}"},"poll":{"enabled":true,"overlapSeconds":0}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(Secret, h.Secrets.Values[InboundSecretName]);
        JsonObject poll = Section(h.Profiles.Stored!.InboundSettings, "poll");
        Assert.True((bool)poll["enabled"]!);
        Assert.Equal(0, (int)poll["overlapSeconds"]!);
    }

    [Fact]
    public async Task Without_a_stored_profile_every_poll_field_is_judged()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: """{"poll":{"enabled":true,"overlapSeconds":0}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("InboundSettings.poll.overlapSeconds", result.Message);
    }

    [Fact]
    public async Task Poll_stored_for_another_adapter_does_not_count_as_already_stored()
    {
        var h = new Harness(stored: Profile(inbound: """{"poll":{"enabled":true,"overlapSeconds":0}}""") with { InboundAdapter = "iScala" });

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: """{"poll":{"enabled":true,"overlapSeconds":0}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("InboundSettings.poll.overlapSeconds", result.Message);
    }

    [Fact]
    public async Task Settings_without_poll_section_are_saved()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: """{"url":"https://erp/"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
    }

    [Fact]
    public async Task Valid_poll_section_reaches_the_profile_intact()
    {
        var h = new Harness(stored: Profile(inbound: $$$$"""
            {"url":"https://erp/","companies":["brmf"],"auth":{"clientSecretRef":"kv:{{{{InboundSecretName}}}}"},
             "poll":{"enabled":false,"intervalSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}}
            """));

        // Como a tela manda: as settings lidas (sem a referência), só com o enabled trocado.
        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: """
            {"url":"https://erp/","companies":["brmf"],"auth":{},
             "poll":{"enabled":true,"intervalSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}}
            """));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        JsonObject root = JsonNode.Parse(h.Profiles.Stored!.InboundSettings)!.AsObject();
        Assert.Equal("https://erp/", (string?)root["url"]);
        Assert.Equal("brmf", (string?)root["companies"]![0]);
        Assert.Equal($"kv:{InboundSecretName}", (string?)root["auth"]!["clientSecretRef"]);
        JsonObject poll = root["poll"]!.AsObject();
        Assert.True((bool)poll["enabled"]!);
        Assert.Equal(300, (int)poll["intervalSeconds"]!);
        Assert.Equal("2015-01-01T00:00:00Z", (string?)poll["startFrom"]);
    }

    [Fact]
    public async Task Write_field_that_is_not_text_is_refused()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: """{"token":{"url":"x"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.token", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task Path_that_does_not_fit_a_vault_name_is_refused()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: $$$$"""{"meu_ambiente":{"apiKey":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.meu_ambiente.apiKey", result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.DoesNotContain(Secret, result.Message);
    }

    [Fact]
    public async Task Vault_failure_leaves_the_profile_intact_with_a_message_without_the_value()
    {
        var h = new Harness(stored: Profile());
        TenantConnectorProfile before = h.Profiles.Stored!;
        h.Secrets.FailOnSet = true;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: $$$$"""{"apiKey":"{{{{Secret}}}}"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.SecretStoreFailed, result.Status);
        Assert.Contains("cofre", result.Message);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.apiKey", result.Message);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
    }

    [Fact]
    public async Task Observers_are_told_after_the_vault_write_and_the_upsert()
    {
        var h = new Harness();

        await h.Service.SaveAsync(Request(outbound: $$$$"""{"apiKey":"{{{{Secret}}}}"}"""));

        Assert.Equal(["tenant-a"], h.Observer.Tenants);
        Assert.Equal(1, h.Observer.UpsertsSeen[0]);   // avisou depois do upsert
        Assert.Equal(1, h.Observer.SetsSeen[0]);      // e depois da escrita no cofre
    }

    [Fact]
    public async Task Saving_the_profile_even_unchanged_makes_the_establishment_resolver_list_again()
    {
        // O resolvedor do de/para do estabelecimento é observador como o freio e o token (platform-establishment-resolution,
        // D8): salvar sem mudar nada também o faz reler a plataforma, porque a correção pode ter sido feita lá.
        var h = new Harness();
        var listing = new CountingListing();
        var resolver = new PlatformEstablishmentResolver([listing], new PlatformEstablishmentOptions(), TimeProvider.System);
        var service = new ConnectorProfileService(h.Profiles, h.Secrets, [resolver], Declarations, new Tenant("tenant-a"));
        TenantConnectorProfile profile = Profile(outbound: "{}");

        await resolver.GetAsync(profile, "Avalara");
        await service.SaveAsync(Request());
        await resolver.GetAsync(profile, "Avalara");
        await service.SaveAsync(Request());   // a mesma requisição: nada muda, e o aviso vale do mesmo jeito
        await resolver.GetAsync(profile, "Avalara");

        Assert.Equal(3, listing.Calls);
    }

    [Fact]
    public async Task Observers_are_told_even_when_the_upsert_fails_after_a_vault_write()
    {
        var h = new Harness();
        h.Profiles.FailUpsert = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Service.SaveAsync(Request(outbound: $$$$"""{"apiKey":"{{{{Secret}}}}"}""")));

        Assert.Equal(Secret, h.Secrets.Values[ConnectorSecretName]);   // o segredo já mudou no cofre
        Assert.Equal(["tenant-a"], h.Observer.Tenants);
    }

    [Fact]
    public async Task Support_fields_absent_from_the_request_keep_the_stored_ones()
    {
        const string support = """{"domain":"acme.freshdesk.com","apiKeyRef":"kv:fh-tenant-a--support--apikey"}""";
        var h = new Harness(stored: Profile() with { SupportAdapter = "Local", SupportSettings = support });

        await h.Service.SaveAsync(Request(outbound: """{"usuario":"abc"}"""));

        Assert.Equal("Local", h.Profiles.Stored!.SupportAdapter);
        Assert.Equal(support, h.Profiles.Stored.SupportSettings);
    }

    // ---- Sem campo de tempo real: a integração automática é o poll.enabled (ADR-0029) ----

    // As opções do Host: as da Web mais o conversor de enum (Program.cs).
    private static readonly JsonSerializerOptions HostJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public void Put_body_with_realtime_from_an_old_client_is_read_and_the_field_ignored()
    {
        ConnectorProfileRequest? request = JsonSerializer.Deserialize<ConnectorProfileRequest>("""
            {"environment":"Sandbox","realtime":true,"inboundAdapter":"Dynamics365","inboundSettings":"{}",
             "outboundAdapter":"Avalara","outboundSettings":"{}"}
            """, HostJson);

        Assert.NotNull(request);
        Assert.Equal("Dynamics365", request.InboundAdapter);
        Assert.Equal("{}", request.InboundSettings);
    }

    [Fact]
    public async Task Put_body_with_the_environment_is_accepted_and_the_field_goes_nowhere()
    {
        // O ambiente é da instância (change platform-account-and-connectors, D6): um dashboard que não recarregou ainda o
        // manda, e o campo é ignorado, e não 400.
        ConnectorProfileRequest? request = JsonSerializer.Deserialize<ConnectorProfileRequest>("""
            {"environment":"Production","inboundAdapter":"Dynamics365","inboundSettings":"{}",
             "outboundAdapter":"Avalara","outboundSettings":"{}"}
            """, HostJson);
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request!);
        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        JsonObject stored = JsonSerializer.SerializeToNode(h.Profiles.Stored, HostJson)!.AsObject();
        JsonObject read = JsonSerializer.SerializeToNode(view, HostJson)!.AsObject();
        Assert.DoesNotContain(stored, p => string.Equals(p.Key, "environment", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(read, p => string.Equals(p.Key, "environment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Read_of_the_profile_has_no_realtime_field()
    {
        var h = new Harness(stored: Profile(inbound: """{"poll":{"enabled":true}}"""));

        ConnectorProfileView? view = await h.Service.GetAsync();

        JsonObject json = JsonSerializer.SerializeToNode(view, HostJson)!.AsObject();
        Assert.DoesNotContain(json, p => string.Equals(p.Key, "realtime", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"enabled\":true", (string?)json["inboundSettings"]);   // o estado está nas settings, e só nelas
    }

    [Fact]
    public void Request_to_string_does_not_print_the_settings()
    {
        ConnectorProfileRequest request = WithAccount(clientSecret: Secret, connectorSettings: $$$$"""{"apiKey":"{{{{Secret}}}}"}""") with
        {
            InboundSettings = $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}""",
            SupportSettings = $$$$"""{"apiKey":"{{{{Secret}}}}"}""",
        };

        string text = request.ToString();

        Assert.DoesNotContain(Secret, text);
        Assert.Contains("Avalara", text);
    }

    // ---- Módulos: vêm dos conectores, e não de uma lista do tenant (change platform-account-and-connectors, D7) ----

    [Theory]
    [InlineData("""["Fiscal","Contabil"]""")]
    [InlineData("""["Fiscal","Folha"]""")]
    [InlineData("[]")]
    public async Task An_old_body_with_the_tenant_modules_is_refused_asking_to_reload_the_screen(string modules)
    {
        // Presente e ignorado faria o operador acreditar que configurou algo: o dashboard que não recarregou recebe 400,
        // como o da seção por ambiente (grupo 3), e não um sucesso em que nada aconteceu. Ausente continua mantendo o gravado.
        ConnectorProfileRequest? request = JsonSerializer.Deserialize<ConnectorProfileRequest>($$"""
            {"inboundAdapter":"Dynamics365","inboundSettings":"{}","modules":{{modules}}}
            """, HostJson);
        var h = new Harness(stored: StoredWithModules(TenantModules.Fiscal, TenantModules.Inventory));

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request!);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("modules: a lista de módulos não é mais do tenant", result.Message);
        Assert.Contains("recarregue a tela", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Equal(["Fiscal", "Inventario"], Enabled(h.Profiles.Stored!));
    }

    [Fact]
    public async Task A_body_without_the_tenant_modules_keeps_the_connector_modules()
    {
        var h = new Harness(stored: StoredWithModules(TenantModules.Fiscal, TenantModules.Inventory));

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request());

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(["Fiscal", "Inventario"], Enabled(h.Profiles.Stored!));
        Assert.Equal(["Fiscal", "Inventario"], (await h.Service.GetAsync())!.Modules);
    }

    [Fact]
    public async Task Read_returns_the_union_of_the_connector_modules_and_only_fiscal_without_any()
    {
        var withModules = new Harness(stored: StoredWithModules(TenantModules.Inventory, TenantModules.Fiscal));
        var without = new Harness(stored: Profile());

        Assert.Equal(["Fiscal", "Inventario"], (await withModules.Service.GetAsync())!.Modules);
        Assert.Equal(["Fiscal"], (await without.Service.GetAsync())!.Modules);
    }

    // ---- A conta de plataforma e o conector (change platform-account-and-connectors, grupo 4) ----

    private const string AccountSecretName = "fh-tenant-a--platform--avalara--clientsecret";

    [Theory]
    [InlineData("clientSecret")]
    [InlineData("CLIENT_SECRET")]
    public async Task The_platform_secret_in_a_connector_is_refused_naming_the_field(string field)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithAccount(connectorSettings: $$$$"""{"baseUrls":{"v2":"https://avalara/"},"{{{{field}}}}":"{{{{Secret}}}}"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains(field, result.Message);
        Assert.Contains("a credencial da plataforma é da conta", result.Message);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Null(h.Profiles.Stored);
    }

    [Fact]
    public async Task The_client_id_in_a_connector_is_refused_although_it_is_not_a_secret_field()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithAccount(connectorSettings: """{"nivel":{"clientId":"abc"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("clientId", result.Message);
        Assert.Contains("a credencial da plataforma é da conta", result.Message);
        Assert.Null(h.Profiles.Stored);
    }

    [Fact]
    public async Task Another_secret_of_the_connector_goes_to_the_vault_under_the_connector_scope()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithAccount(connectorSettings: $$$$"""{"apiKey":"{{{{Secret}}}}"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(Secret, h.Secrets.Values["fh-tenant-a--connector--avalara--apikey"]);
        PlatformConnector connector = Assert.Single(Assert.Single(h.Profiles.Stored!.Accounts).Connectors);
        Assert.Equal("kv:fh-tenant-a--connector--avalara--apikey", (string?)Root(connector.Settings)["apiKeyRef"]);
        Assert.DoesNotContain(Secret, connector.Settings);
    }

    [Fact]
    public async Task Two_accounts_of_the_same_platform_are_refused()
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest("Avalara", "abc", Secret), new PlatformAccountRequest("avalara", "xyz")]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Avalara", result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Null(h.Profiles.Stored);
    }

    // ---- O nome gravado é o declarado (change platform-account-and-connectors, D17; tarefa 5.3) ----

    [Fact]
    public async Task A_name_in_another_case_is_saved_as_the_declared_one_and_the_exact_read_finds_it()
    {
        // Antes, o nome ia como veio, e um "avalara" deixava a conta invisível às leituras, que comparam exato.
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest(" avalara ", "abc", Secret, Connectors: [new PlatformConnectorRequest("AVALARA", "{}")])]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        PlatformAccount account = Assert.Single(h.Profiles.Stored!.Accounts, a => string.Equals(a.Platform, "Avalara", StringComparison.Ordinal));
        Assert.Single(account.Connectors, c => string.Equals(c.Adapter, "Avalara", StringComparison.Ordinal));
        Assert.Equal(SecretReference.Of(AccountSecretName), account.ClientSecretRef);
    }

    [Fact]
    public async Task A_name_in_another_case_does_not_rename_the_stored_account()
    {
        var h = new Harness(stored: Profile(outbound: "{}"));

        await h.Service.SaveAsync(Request(outbound: "{}", outboundAdapter: "avalara", connectorAdapter: "avalara"));

        PlatformAccount account = Assert.Single(h.Profiles.Stored!.Accounts);
        Assert.Equal("Avalara", account.Platform);
        Assert.Equal("Avalara", Assert.Single(account.Connectors).Adapter);
    }

    [Theory]
    [InlineData("Mock")]
    [InlineData("Sovos")]
    public async Task A_platform_no_implementation_declares_is_refused_naming_it_and_the_declared_ones(string platform)
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest(platform, "abc", Secret, Connectors: [new PlatformConnectorRequest(platform, "{}")])]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains($"'{platform}'", result.Message);
        Assert.Contains("Avalara, Outra", result.Message);   // as declaradas
        Assert.Empty(h.Secrets.Values);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Theory]
    [InlineData("Sovos")]   // que ninguém declara
    [InlineData("Outro")]   // que outra plataforma declara
    public async Task An_adapter_the_platform_of_the_account_does_not_declare_is_refused_naming_it_and_the_declared_ones(string adapter)
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest("Avalara", "abc", Secret, Connectors: [new PlatformConnectorRequest(adapter, "{}")])]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains($"'{adapter}'", result.Message);
        Assert.Contains("plataforma 'Avalara'", result.Message);
        Assert.Contains("Os declarados são: Avalara.", result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    // ---- O mapa de URLs base contra a declaração (tarefa 5.5) ----

    [Theory]
    [InlineData("v3")]
    [InlineData("V2")]   // a chave é exata: a leitura acha a "v2", e não a "V2"
    public async Task A_base_for_a_version_the_adapter_does_not_declare_is_refused_naming_it_and_the_declared_ones(string version)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: $$$"""{"baseUrls":{"v2":"https://avalara/","{{{version}}}":"https://outra/"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains($"Accounts[Avalara].Connectors[Avalara].Settings.baseUrls.{version}", result.Message);
        Assert.Contains("As declaradas são: v2, v1.", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Theory]
    [InlineData("""{"baseUrls":{"v2":"https://avalara/"}}""")]                        // só a base que o Fiscal usa
    [InlineData("""{"baseUrls":{"v2":"https://avalara/","v1":""}}""")]                // a declarada que ninguém usa, vazia
    [InlineData("""{"baseUrls":{"v2":"https://avalara/","v1":null}}""")]
    [InlineData("""{"baseUrls":{}}""")]
    [InlineData("""{"establishments":{}}""")]                                           // sem o mapa
    public async Task Declared_versions_are_accepted_even_empty(string settings)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: settings));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
    }

    [Theory]
    [InlineData("""{"baseUrls":"https://avalara/"}""", "Settings.baseUrls")]
    [InlineData("""{"baseUrls":["https://avalara/"]}""", "Settings.baseUrls")]
    [InlineData("""{"baseUrls":{"v2":5}}""", "Settings.baseUrls.v2")]
    public async Task A_base_url_map_that_is_not_version_to_text_is_refused(string settings, string field)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: settings));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains(field, result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    // ---- O https na gravação, e não só na leitura (tarefa 5.6) ----

    [Theory]
    [InlineData("http://sandbox.exemplo.com/")]
    [InlineData("ftp://avalara/")]
    [InlineData("avalara/")]
    public async Task A_base_url_outside_the_https_rule_is_refused_naming_the_field(string url)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithAccount(clientSecret: Secret, connectorSettings: $$$"""{"baseUrls":{"v2":"{{{url}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.baseUrls.v2", result.Message);
        Assert.Contains("https", result.Message);
        Assert.DoesNotContain(url, result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Theory]
    [InlineData("http://login.exemplo.com/oauth/token")]
    [InlineData("oauth/token")]
    public async Task A_token_url_outside_the_https_rule_is_refused_naming_the_field(string url)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithAccount(clientSecret: Secret, tokenUrl: url));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].tokenUrl", result.Message);
        Assert.Contains("https", result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Theory]
    [InlineData("http://localhost:5100/")]
    [InlineData("http://127.0.0.1:5100/")]
    [InlineData("https://api-gateway.avalarabrasil.com.br/")]
    public async Task Https_and_loopback_http_are_accepted_in_the_base_and_in_the_token_url(string url)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithAccount(tokenUrl: url + "oauth/token", connectorSettings: $$$"""{"baseUrls":{"v2":"{{{url}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
    }

    // ---- Módulos e integrações contra a declaração do adapter (tarefa 6.1) ----

    [Theory]
    [InlineData("Inventario")]    // do vocabulário do hub, mas este adapter não o declara
    [InlineData("Faturamento")]   // que ninguém declara
    public async Task A_module_the_adapter_does_not_declare_is_refused_naming_the_adapter_and_the_module(string module)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModules([module]));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Modules", result.Message);
        Assert.Contains($"o adapter 'Avalara' não declara o módulo '{module}'", result.Message);
        Assert.Contains("Os declarados são: Fiscal, Contabil.", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task An_integration_the_adapter_does_not_declare_is_refused_naming_the_adapter_and_the_integration()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithModules([TenantModules.Fiscal], new ConnectorIntegrationRequest(ConnectorIntegrations.BlockK200, "k200")));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Integrations[k200]", result.Message);
        Assert.Contains("o adapter 'Avalara' não declara a integração 'k200'", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task An_integration_without_its_module_is_refused()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithModules([TenantModules.Fiscal], new ConnectorIntegrationRequest(ConnectorIntegrations.ChartOfAccounts, "plano")));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Integrations[plano-de-contas]", result.Message);
        Assert.Contains("é do módulo 'Contabil', que não está ligado neste conector", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task A_module_on_without_any_integration_is_accepted()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModules([TenantModules.Accounting]));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(["Contabil"], Enabled(h.Profiles.Stored!));
    }

    [Fact]
    public async Task Modules_and_integrations_in_another_case_are_saved_as_declared()
    {
        // Módulo e integração são identidade, de um conjunto fechado que o adapter declara: canonizados, como a plataforma e
        // o adapter (D17), e não recusados, como a chave de versão do blob (D4).
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithModules(["fiscal", "CONTABIL"], new ConnectorIntegrationRequest("NOTAS-FISCAIS", "taxcompliance/v2/fiscal/dfe")));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(["Fiscal/notas-fiscais=taxcompliance/v2/fiscal/dfe", "Contabil"], Enabled(h.Profiles.Stored!));
    }

    [Fact]
    public async Task The_same_integration_twice_in_a_connector_is_refused()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModules(
            [TenantModules.Fiscal],
            new ConnectorIntegrationRequest(ConnectorIntegrations.Invoices, "a"),
            new ConnectorIntegrationRequest("Notas-Fiscais", "b")));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("a integração 'notas-fiscais' aparece 2 vezes", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task Without_modules_and_integrations_the_connector_keeps_the_stored_ones()
    {
        // Um dashboard que ainda não manda o que o conector liga não desliga nada ao salvar.
        TenantConnectorProfile stored = Profile(outbound: "{}") with
        {
            Accounts =
            [
                new PlatformAccount
                {
                    Platform = "Avalara",
                    Connectors =
                    [
                        new PlatformConnector
                        {
                            Adapter = "Avalara",
                            Modules = [new ConnectorModule
                            {
                                Module = TenantModules.Fiscal,
                                Integrations = [new ConnectorIntegration { Key = ConnectorIntegrations.Invoices, Endpoint = "dfe" }],
                            }],
                        },
                    ],
                },
            ],
        };
        var h = new Harness(stored: stored);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: """{"baseUrls":{"v2":"https://avalara/"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(["Fiscal/notas-fiscais=dfe"], Enabled(h.Profiles.Stored!));
    }

    // ---- No máximo um conector por integração, no tenant (tarefa 6.4) ----

    [Fact]
    public async Task Two_connectors_with_the_same_integration_are_refused_naming_it_and_both()
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts:
            [
                new PlatformAccountRequest("Avalara", "abc", Connectors:
                    [new PlatformConnectorRequest("Avalara", BothBases, [TenantModules.Fiscal], [new(ConnectorIntegrations.Invoices, "a")])]),
                new PlatformAccountRequest("Outra", "xyz", Connectors:
                    [new PlatformConnectorRequest("Outro", """{"baseUrls":{"v2":"https://outra/"}}""", [TenantModules.Fiscal], [new(ConnectorIntegrations.Invoices, "b")])]),
            ]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("a integração 'notas-fiscais' está ligada em mais de um conector", result.Message);
        Assert.Contains("'Avalara' (conta 'Avalara')", result.Message);
        Assert.Contains("'Outro' (conta 'Outra')", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task Two_distinct_integrations_of_the_same_module_in_two_connectors_are_accepted()
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts:
            [
                new PlatformAccountRequest("Avalara", "abc", Connectors:
                    [new PlatformConnectorRequest("Avalara", BothBases, [TenantModules.Fiscal], [new(ConnectorIntegrations.Invoices, "a")])]),
                new PlatformAccountRequest("Outra", "xyz", Connectors:
                    [new PlatformConnectorRequest("Outro", """{"baseUrls":{"v2":"https://outra/"}}""", [TenantModules.Fiscal], [new("emissao", "b")])]),
            ]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.True(result.Status == ConnectorProfileSaveStatus.Saved, result.Message);
    }

    // ---- A versão que uma integração ligada usa precisa da base (tarefa 6.5) ----

    [Fact]
    public async Task An_enabled_integration_whose_version_has_no_base_is_refused_naming_the_version_and_the_integrations()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModulesAndSettings(
            """{"baseUrls":{"v2":"https://avalara/"}}""",
            [TenantModules.Fiscal, TenantModules.Accounting],
            new ConnectorIntegrationRequest(ConnectorIntegrations.Invoices, "dfe"),
            new ConnectorIntegrationRequest(ConnectorIntegrations.ChartOfAccounts, ""),
            new ConnectorIntegrationRequest(ConnectorIntegrations.JournalEntries, "")));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Settings.baseUrls.v1", result.Message);
        Assert.Contains("plano-de-contas, lancamentos", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task A_module_on_without_integrations_does_not_require_the_version_of_its_integrations()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModulesAndSettings(
            """{"baseUrls":{"v2":"https://avalara/"}}""",
            [TenantModules.Fiscal, TenantModules.Accounting],
            new ConnectorIntegrationRequest(ConnectorIntegrations.Invoices, "dfe")));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
    }

    // ---- O tamanho de lote, onde a API aceita (tarefa 6.6) ----

    [Fact]
    public async Task A_batch_size_where_the_api_takes_no_batch_is_refused_naming_the_integration()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            WithModules([TenantModules.Fiscal], new ConnectorIntegrationRequest(ConnectorIntegrations.Invoices, "dfe", BatchSize: 10)));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors[Avalara].Integrations[notas-fiscais].batchSize", result.Message);
        Assert.Contains("não aceita lote", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task Each_batch_integration_keeps_its_own_size()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModules(
            [TenantModules.Accounting],
            new ConnectorIntegrationRequest(ConnectorIntegrations.ChartOfAccounts, "plano", BatchSize: 100),
            new ConnectorIntegrationRequest(ConnectorIntegrations.JournalEntries, "lancamentos", BatchSize: 50)));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal([100, 50], h.Profiles.Stored!.Accounts.Single().Connectors.Single().Modules.Single().Integrations.Select(i => i.BatchSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_batch_size_that_is_not_positive_is_refused(int size)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModules(
            [TenantModules.Accounting], new ConnectorIntegrationRequest(ConnectorIntegrations.ChartOfAccounts, "plano", BatchSize: size)));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Integrations[plano-de-contas].batchSize", result.Message);
        Assert.Contains("positivo", result.Message);
    }

    [Fact]
    public async Task The_read_has_the_modules_and_the_integrations_and_no_batch_field_where_the_api_takes_no_batch()
    {
        // O lote na integração de notas só chega por fora da gravação — o SQL direto, que ela recusaria: a leitura confere
        // contra a declaração, e não confia no que está gravado.
        TenantConnectorProfile stored = Profile() with
        {
            Accounts =
            [
                new PlatformAccount
                {
                    Platform = "Avalara",
                    Connectors =
                    [
                        new PlatformConnector
                        {
                            Adapter = "Avalara",
                            Settings = BothBases,
                            Modules =
                            [
                                new ConnectorModule
                                {
                                    Module = TenantModules.Fiscal,
                                    Integrations = [new ConnectorIntegration { Key = ConnectorIntegrations.Invoices, Endpoint = "dfe", BatchSize = 10 }],
                                },
                                new ConnectorModule
                                {
                                    Module = TenantModules.Accounting,
                                    Integrations = [new ConnectorIntegration { Key = ConnectorIntegrations.ChartOfAccounts, Endpoint = "plano", BatchSize = 100 }],
                                },
                            ],
                        },
                    ],
                },
            ],
        };
        var h = new Harness(stored: stored);

        PlatformConnectorView connector = ReadConnector(await h.Service.GetAsync());
        JsonObject json = JsonSerializer.SerializeToNode(connector, HostJson)!.AsObject();

        Assert.Equal(["Fiscal", "Contabil"], connector.Modules);
        JsonObject invoices = json["integrations"]!.AsArray().Single(i => (string?)i!["key"] == "notas-fiscais")!.AsObject();
        JsonObject chart = json["integrations"]!.AsArray().Single(i => (string?)i!["key"] == "plano-de-contas")!.AsObject();
        Assert.False(invoices.ContainsKey("batchSize"));
        Assert.Equal(100, (int?)chart["batchSize"]);
        Assert.Equal("Fiscal", (string?)invoices["module"]);
    }

    // ---- A integração sem caminho fica inativa (tarefa 6.7) ----

    [Fact]
    public async Task Integrations_with_a_blank_path_are_saved_and_the_read_says_they_have_no_path()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithModules(
            [TenantModules.Fiscal, TenantModules.Accounting],
            new ConnectorIntegrationRequest(ConnectorIntegrations.Invoices, "dfe"),
            new ConnectorIntegrationRequest(ConnectorIntegrations.ChartOfAccounts, ""),
            new ConnectorIntegrationRequest(ConnectorIntegrations.JournalEntries, "  ")));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        PlatformConnectorView connector = ReadConnector(await h.Service.GetAsync());
        Assert.Equal(
            ["notas-fiscais ativa", "plano-de-contas sem caminho", "lancamentos sem caminho"],
            connector.Integrations.Select(i => $"{i.Key} {(i.WithoutPath ? "sem caminho" : "ativa")}"));
    }

    // ---- Um conector por adapter na conta (tarefa 5.4) ----

    [Theory]
    [InlineData("Avalara")]
    [InlineData("avalara")]   // a mesma, na forma declarada
    public async Task Two_connectors_with_the_same_adapter_in_one_account_are_refused_naming_the_adapter(string second)
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts:
            [
                new PlatformAccountRequest("Avalara", "abc", Connectors:
                [
                    new PlatformConnectorRequest("Avalara", """{"apiKey":"k3y"}"""),
                    new PlatformConnectorRequest(second, "{}"),
                ]),
            ]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("Accounts[Avalara].Connectors", result.Message);
        Assert.Contains("o adapter 'Avalara' aparece 2 vezes", result.Message);
        Assert.Empty(h.Secrets.Values);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task The_account_secret_goes_to_the_vault_under_the_platform_name_and_the_account_keeps_the_reference()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithAccount(clientSecret: Secret));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(Secret, h.Secrets.Values[AccountSecretName]);
        PlatformAccount account = Assert.Single(h.Profiles.Stored!.Accounts);
        Assert.Equal("Avalara", account.Platform);
        Assert.Equal("abc", account.ClientId);
        Assert.Equal($"kv:{AccountSecretName}", account.ClientSecretRef);
        Assert.Equal("https://login.exemplo/oauth/token", account.TokenUrl);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(h.Profiles.Stored, HostJson));
    }

    [Fact]
    public async Task Saving_the_account_without_the_secret_keeps_the_stored_reference_without_touching_the_vault()
    {
        var h = new Harness(stored: Profile() with { Accounts = [StoredAccount()] });

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithAccount(clientId: "xyz"));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(0, h.Secrets.SetCount);
        PlatformAccount account = Assert.Single(h.Profiles.Stored!.Accounts);
        Assert.Equal("xyz", account.ClientId);
        Assert.Equal($"kv:{AccountSecretName}", account.ClientSecretRef);
    }

    [Fact]
    public async Task A_reference_in_the_account_request_is_refused()
    {
        var h = new Harness();
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest("Avalara", "abc", ClientSecretRef: "kv:fh-tenant-b--platform--avalara--clientsecret")]);

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(request);

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("clientSecretRef", result.Message);
        Assert.Null(h.Profiles.Stored);
    }

    [Fact]
    public async Task The_mask_as_the_account_secret_is_refused()
    {
        var h = new Harness(stored: Profile() with { Accounts = [StoredAccount()] });
        h.Secrets.Values[AccountSecretName] = Secret;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(WithAccount(clientSecret: "••••••••"));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("máscara", result.Message);
        Assert.Equal(Secret, h.Secrets.Values[AccountSecretName]);
    }

    [Fact]
    public async Task Saving_without_accounts_keeps_the_stored_ones()
    {
        // Um dashboard que não recarregou não manda a conta: a saída do tenant não é apagada por isso.
        var h = new Harness(stored: Profile() with { Accounts = [StoredAccount()] });

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(inbound: """{"url":"https://erp/"}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(StoredAccount(), Assert.Single(h.Profiles.Stored!.Accounts), new AccountComparer());
    }

    [Fact]
    public async Task Saving_an_account_without_connectors_keeps_the_stored_connectors()
    {
        var h = new Harness(stored: Profile() with { Accounts = [StoredAccount()] });
        var request = new ConnectorProfileRequest(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest("Avalara", "xyz", TokenUrl: "https://login.exemplo/oauth/token")]);

        await h.Service.SaveAsync(request);

        PlatformConnector connector = Assert.Single(Assert.Single(h.Profiles.Stored!.Accounts).Connectors);
        Assert.Equal("Avalara", connector.Adapter);
        Assert.Contains("establishments", connector.Settings);
    }

    [Fact]
    public async Task Read_returns_the_account_without_the_reference_and_the_secret_state_under_the_platform()
    {
        var h = new Harness(stored: Profile() with { Accounts = [StoredAccount()] });
        h.Secrets.Values[AccountSecretName] = Secret;

        ConnectorProfileView? view = await h.Service.GetAsync();

        PlatformAccountView account = Assert.Single(view!.Accounts!);
        Assert.Equal(("Avalara", "abc", "https://login.exemplo/oauth/token"), (account.Platform, account.ClientId, account.TokenUrl));
        Assert.Equal("Avalara", Assert.Single(account.Connectors).Adapter);
        Assert.Equal(new SecretStatus(true, SavedOn), view.Secrets["platform.Avalara.clientSecret"]);
        string json = JsonSerializer.Serialize(view, HostJson);
        Assert.DoesNotContain(AccountSecretName, json);
        Assert.DoesNotContain(Secret, json);
    }

    private static ConnectorProfileRequest WithAccount(
        string? connectorSettings = """{"baseUrls":{"v2":"https://avalara/"}}""", string? clientId = "abc", string? clientSecret = null,
        string? tokenUrl = "https://login.exemplo/oauth/token")
        => new(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest("Avalara", clientId, clientSecret, tokenUrl, [new PlatformConnectorRequest("Avalara", connectorSettings)])]);

    // As bases das duas versões que o dublê da Avalara declara.
    private const string BothBases = """{"baseUrls":{"v2":"https://avalara/","v1":"https://avalara/"}}""";

    // A conta Avalara com o conector Avalara ligando os módulos e as integrações dados, com as bases das duas versões.
    private static ConnectorProfileRequest WithModules(IReadOnlyList<string>? modules, params ConnectorIntegrationRequest[] integrations)
        => WithModulesAndSettings(BothBases, modules, integrations);

    private static ConnectorProfileRequest WithModulesAndSettings(
        string settings, IReadOnlyList<string>? modules, params ConnectorIntegrationRequest[] integrations)
        => new(
            InboundAdapter: "Dynamics365", InboundSettings: "{}",
            Accounts: [new PlatformAccountRequest("Avalara", "abc", Connectors: [new PlatformConnectorRequest("Avalara", settings, modules, integrations)])]);

    // O conector único da leitura.
    private static PlatformConnectorView ReadConnector(ConnectorProfileView? view) => view!.Accounts.Single().Connectors.Single();

    // O perfil gravado com a conta Avalara e o conector Avalara ligando os módulos dados, sem integração.
    private static TenantConnectorProfile StoredWithModules(params string[] modules) => Profile() with
    {
        Accounts =
        [
            new PlatformAccount
            {
                Platform = "Avalara",
                Connectors = [new PlatformConnector { Adapter = "Avalara", Modules = [.. modules.Select(m => new ConnectorModule { Module = m })] }],
            },
        ],
    };

    // O que o conector único liga: "Módulo/integração=caminho", ou só "Módulo" quando ele não tem integração.
    private static string[] Enabled(TenantConnectorProfile profile)
        => [.. profile.Accounts.Single().Connectors.Single().Modules.SelectMany(m => m.Integrations.Count == 0
            ? [m.Module]
            : m.Integrations.Select(i => $"{m.Module}/{i.Key}={i.Endpoint}"))];

    private static PlatformAccount StoredAccount() => new()
    {
        Platform = "Avalara",
        ClientId = "abc",
        ClientSecretRef = $"kv:{AccountSecretName}",
        TokenUrl = "https://login.exemplo/oauth/token",
        Connectors = [new PlatformConnector { Adapter = "Avalara", Settings = """{"baseUrls":{"v2":"https://avalara/"},"establishments":{}}""" }],
    };

    // Registros com lista não comparam por valor: a conta é igual quando os campos e os conectores são.
    private sealed class AccountComparer : IEqualityComparer<PlatformAccount>
    {
        public bool Equals(PlatformAccount? x, PlatformAccount? y)
            => x is not null && y is not null && x with { Connectors = [] } == y with { Connectors = [] } && x.Connectors.SequenceEqual(y.Connectors);

        public int GetHashCode(PlatformAccount obj) => obj.Platform.GetHashCode(StringComparison.Ordinal);
    }

    // ---- Leitura ----

    [Fact]
    public async Task Read_returns_the_settings_without_the_references()
    {
        var h = new Harness(stored: Profile(
            outbound: $$$$$"""{"usuario":"abc","apiKeyRef":"kv:{{{{{ConnectorSecretName}}}}}","establishments":{"1":{"codigoEmpresa":"2"}}}"""));

        ConnectorProfileView? view = await h.Service.GetAsync();

        JsonObject outbound = Root(view!.Accounts.Single().Connectors.Single().Settings);
        Assert.False(outbound.ContainsKey("apiKeyRef"));
        Assert.Equal("abc", (string?)outbound["usuario"]);
        Assert.Equal("2", (string?)outbound["establishments"]!["1"]!["codigoEmpresa"]);
    }

    [Fact]
    public async Task Secrets_map_says_configured_with_the_date_of_the_current_version()
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"apiKeyRef":"kv:{{{{ConnectorSecretName}}}}"}"""));
        h.Secrets.Values[ConnectorSecretName] = Secret;

        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(new SecretStatus(true, SavedOn), view!.Secrets["connector.Avalara.apiKey"]);
        Assert.Equal(0, h.Secrets.GetCount);   // a leitura descreve, e nunca lê o valor
    }

    [Fact]
    public async Task Reference_without_value_in_the_vault_is_not_configured()
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"apiKeyRef":"kv:{{{{ConnectorSecretName}}}}"}"""));

        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(new SecretStatus(false, null), view!.Secrets["connector.Avalara.apiKey"]);
    }

    [Theory]
    [InlineData("kv:fh-tenant-b--connector--avalara--apikey")]   // de outro tenant
    [InlineData("s3cr3t")]                                           // malformada
    public async Task Foreign_or_malformed_reference_is_not_configured_and_the_vault_is_not_asked(string reference)
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"apiKeyRef":"{{{{reference}}}}"}"""));
        h.Secrets.Values["fh-tenant-b--connector--avalara--apikey"] = "do-outro";

        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(new SecretStatus(false, null), view!.Secrets["connector.Avalara.apiKey"]);
        Assert.Equal(0, h.Secrets.DescribeCount);
    }

    [Fact]
    public async Task Read_never_contains_the_value_part_of_it_or_the_reference()
    {
        var h = new Harness(stored: Profile(
            inbound: $$$$"""{"auth":{"clientId":"app","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"}}""",
            // Segredo em claro gravado por SQL direto: a leitura também não o devolve.
            outbound: $$$$"""{"apiKeyRef":"kv:{{{{ConnectorSecretName}}}}","password":"{{{{Secret}}}}"}""")
            with { SupportSettings = $$$$"""{"apiKeyRef":"kv:fh-tenant-a--support--apikey"}""" });
        h.Secrets.Values[ConnectorSecretName] = Secret;
        h.Secrets.Values["fh-tenant-a--inbound--auth--clientsecret"] = Secret;

        ConnectorProfileView? view = await h.Service.GetAsync();
        string json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain(Secret, json);
        Assert.DoesNotContain(Secret[^4..], json);   // nem os últimos 4
        Assert.DoesNotContain("kv:", json);
        Assert.DoesNotContain("fh-tenant-a", json);
        Assert.Equal(["connector.Avalara.apiKey", "inbound.auth.clientSecret", "platform.Avalara.clientSecret", "support.apiKey"],
            view!.Secrets.Keys.Order());
    }

    [Fact]
    public async Task Read_of_a_tenant_without_profile_is_null()
    {
        var h = new Harness();

        Assert.Null(await h.Service.GetAsync());
    }

    // ---- Apoio ----

    // A requisição com as settings de um conector (outbound), sob a conta da mesma plataforma; sem elas, a requisição não traz
    // conta nenhuma, e a gravada fica.
    private static ConnectorProfileRequest Request(
        string? outbound = null, string? inbound = null, string outboundAdapter = "Avalara", string? connectorAdapter = null)
        => new("Dynamics365", inbound, Accounts: outbound is null
            ? null
            : [new PlatformAccountRequest(outboundAdapter, Connectors: [new PlatformConnectorRequest(connectorAdapter ?? outboundAdapter, outbound)])]);

    private static ConnectorProfileRequest RequestWith(ConnectorSettingsKind kind, string json) => kind switch
    {
        ConnectorSettingsKind.Inbound => Request(inbound: json),
        ConnectorSettingsKind.Connector => Request(outbound: json),
        _ => Request() with { SupportAdapter = "Local", SupportSettings = json },
    };

    // O perfil gravado; com as settings de um conector (outbound), a conta Avalara e o conector Avalara com elas.
    private static TenantConnectorProfile Profile(string tenant = "tenant-a", string inbound = "{}", string? outbound = null) => new()
    {
        TenantId = tenant,
        InboundAdapter = "Dynamics365",
        InboundSettings = inbound,
        Accounts = outbound is null
            ? []
            : [new PlatformAccount { Platform = "Avalara", Connectors = [new PlatformConnector { Adapter = "Avalara", Settings = outbound }] }],
    };

    private static string SettingsOf(TenantConnectorProfile profile, ConnectorSettingsKind kind) => kind switch
    {
        ConnectorSettingsKind.Inbound => profile.InboundSettings,
        ConnectorSettingsKind.Connector => ConnectorSettings(profile),
        _ => profile.SupportSettings,
    };

    private static string ConnectorSettings(TenantConnectorProfile profile) => profile.Accounts.Single().Connectors.Single().Settings;

    private static JsonObject Section(string json, string name) => (JsonObject)JsonNode.Parse(json)![name]!;

    private static JsonObject Root(string json) => JsonNode.Parse(json)!.AsObject();

    // As implementações registradas: a Avalara, e uma segunda plataforma, com outro adapter, para provar que a conta só
    // aceita os adapters que a plataforma dela declara (D17).
    private static readonly IAdapterDeclaration[] Declarations =
    [
        new FakeDeclaration("Avalara", "Avalara")
        {
            Modules =
            [
                new(TenantModules.Fiscal, [new(ConnectorIntegrations.Invoices, "v2", AcceptsBatch: false)]),
                new(TenantModules.Accounting,
                [
                    new(ConnectorIntegrations.ChartOfAccounts, "v1", AcceptsBatch: true),
                    new(ConnectorIntegrations.JournalEntries, "v1", AcceptsBatch: true),
                ]),
            ],
        },
        // Uma segunda integração fiscal, de chave própria, só deste dublê: duas integrações distintas do mesmo módulo, em
        // conectores diferentes, são aceitas (6.4).
        new FakeDeclaration("Outra", "Outro")
        {
            Modules =
            [
                new(TenantModules.Fiscal,
                [
                    new(ConnectorIntegrations.Invoices, "v2", AcceptsBatch: false),
                    new("emissao", "v2", AcceptsBatch: false),
                ]),
            ],
        },
    ];

    private sealed record FakeDeclaration(string Platform, string Adapter) : IAdapterDeclaration
    {
        public IReadOnlyList<DeclaredModule> Modules { get; init; } = [];
    }

    private sealed class Harness
    {
        public Harness(string tenant = "tenant-a", TenantConnectorProfile? stored = null)
        {
            Profiles = new FakeProfiles { Stored = stored };
            Observer = new RecordingObserver(Secrets, Profiles);
            Service = new ConnectorProfileService(Profiles, Secrets, [Observer], Declarations, new Tenant(tenant));
        }

        public FakeSecrets Secrets { get; } = new();

        public FakeProfiles Profiles { get; }

        public RecordingObserver Observer { get; }

        public ConnectorProfileService Service { get; }
    }

    private sealed class CountingListing : IPlatformEstablishmentListing
    {
        public string Adapter => "Avalara";

        public int Calls { get; private set; }

        public Task<IReadOnlyList<PlatformEstablishment>> ListAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PlatformEstablishment>>([]);
        }
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class FakeSecrets : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public bool FailOnSet { get; set; }

        public int SetCount { get; private set; }

        public int GetCount { get; private set; }

        public int DescribeCount { get; private set; }

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
        {
            GetCount++;
            return Task.FromResult(Values.GetValueOrDefault(name));
        }

        public Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            if (FailOnSet)
            {
                throw new InvalidOperationException($"O cofre não conseguiu gravar o segredo '{name}' (HTTP 503).");
            }

            SetCount++;
            Values[name] = value;
            return Task.CompletedTask;
        }

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default)
        {
            DescribeCount++;
            return Task.FromResult(Values.ContainsKey(name) ? new SecretDescription(SavedOn) : null);
        }
    }

    private sealed class FakeProfiles : IConnectorProfileStore
    {
        public TenantConnectorProfile? Stored { get; set; }

        public bool FailUpsert { get; set; }

        public int UpsertCount { get; private set; }

        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(Stored?.TenantId == tenantId ? Stored : null);

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        {
            if (FailUpsert)
            {
                throw new InvalidOperationException("banco fora do ar");
            }

            UpsertCount++;
            Stored = profile;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingObserver(FakeSecrets secrets, FakeProfiles profiles) : IConnectorProfileObserver
    {
        public List<string> Tenants { get; } = [];

        public List<int> SetsSeen { get; } = [];

        public List<int> UpsertsSeen { get; } = [];

        public Task ProfileSavedAsync(string tenantId, CancellationToken ct = default)
        {
            Tenants.Add(tenantId);
            SetsSeen.Add(secrets.SetCount);
            UpsertsSeen.Add(profiles.UpsertCount);
            return Task.CompletedTask;
        }
    }
}
