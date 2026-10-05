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
    private const string SandboxSecretName = "fh-tenant-a--outbound--sandbox--clientsecret";
    private static readonly DateTimeOffset SavedOn = new(2026, 9, 27, 14, 30, 0, TimeSpan.Zero);

    // ---- Gravação ----

    [Fact]
    public async Task Write_field_goes_to_the_vault_and_the_profile_keeps_only_the_reference()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: $$$$"""{"sandbox":{"clientId":"abc","clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(Secret, h.Secrets.Values[SandboxSecretName]);
        JsonObject sandbox = Section(h.Profiles.Stored!.OutboundSettings, "sandbox");
        Assert.Equal($"kv:{SandboxSecretName}", (string?)sandbox["clientSecretRef"]);
        Assert.Equal("abc", (string?)sandbox["clientId"]);
        Assert.False(sandbox.ContainsKey("clientSecret"));
        Assert.DoesNotContain(Secret, h.Profiles.Stored.OutboundSettings);
        Assert.Equal("tenant-a", h.Profiles.Stored.TenantId);   // o tenant é o do login, e não vem do corpo
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
        foreach (ConnectorSettingsKind kind in Enum.GetValues<ConnectorSettingsKind>())
        {
            var h = new Harness();
            string json = $$$$"""{"nivel":{"fundo":{"{{{{field}}}}":"{{{{Secret}}}}"}}}""";

            ConnectorProfileSaveResult result = await h.Service.SaveAsync(RequestWith(kind, json));

            Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
            string name = $"fh-tenant-a--{kind.ToString().ToLowerInvariant()}--nivel--fundo--{segment}";
            Assert.Equal(Secret, h.Secrets.Values[name]);
            string persisted = SettingsOf(h.Profiles.Stored!, kind);
            JsonObject fundo = (JsonObject)Section(persisted, "nivel")["fundo"]!;
            Assert.Equal($"kv:{name}", (string?)fundo[field + "Ref"]);
            Assert.False(fundo.ContainsKey(field));
            Assert.DoesNotContain(Secret, persisted);
        }
    }

    [Theory]
    [InlineData("""{"sandbox":{"clientId":"xyz"}}""")]
    [InlineData("""{"sandbox":{"clientId":"xyz","clientSecret":""}}""")]
    [InlineData("""{"sandbox":{"clientId":"xyz","clientSecret":null}}""")]
    public async Task Absent_write_field_keeps_the_stored_reference_without_touching_the_vault(string outbound)
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"sandbox":{"clientId":"abc","clientSecretRef":"kv:{{{{SandboxSecretName}}}}"}}"""));

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: outbound));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(0, h.Secrets.SetCount);
        JsonObject sandbox = Section(h.Profiles.Stored!.OutboundSettings, "sandbox");
        Assert.Equal($"kv:{SandboxSecretName}", (string?)sandbox["clientSecretRef"]);
        Assert.Equal("xyz", (string?)sandbox["clientId"]);
        Assert.False(sandbox.ContainsKey("clientSecret"));
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
        var h = new Harness(stored: Profile(outbound: $$$$"""{"sandbox":{"clientId":"abc","clientSecretRef":"kv:{{{{SandboxSecretName}}}}"}}"""));
        h.Secrets.Values[SandboxSecretName] = Secret;
        TenantConnectorProfile before = h.Profiles.Stored!;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: "{\"sandbox\":{\"clientId\":\"abc\",\"clientSecret\":\"" + mask + "\"}}"));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("OutboundSettings.sandbox.clientSecret", result.Message);
        Assert.Contains("máscara", result.Message);
        Assert.DoesNotContain(mask, result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.Equal(Secret, h.Secrets.Values[SandboxSecretName]);   // o segredo gravado continua o mesmo
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

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: """{"sandbox":{"clientSecret":"ab*cd"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal("ab*cd", h.Secrets.Values[SandboxSecretName]);
    }

    [Fact]
    public async Task Stored_reference_is_not_carried_to_another_adapter()
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"sandbox":{"clientSecretRef":"kv:{{{{SandboxSecretName}}}}"}}"""));

        await h.Service.SaveAsync(Request(outbound: """{"sandbox":{"baseUrl":"http://localhost:5100/"}}""", outboundAdapter: "Mock"));

        Assert.False(Section(h.Profiles.Stored!.OutboundSettings, "sandbox").ContainsKey("clientSecretRef"));
    }

    [Theory]
    [InlineData("""{"sandbox":{"clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret"}}""")]   // a de outro tenant
    [InlineData("""{"sandbox":{"clientSecretRef":"kv:fh-tenant-b--outbound--sandbox--clientsecret"}}""")]   // até a do próprio
    [InlineData("""{"sandbox":{"CLIENT_SECRET_REF":"s3cr3t"}}""")]
    public async Task Reference_in_the_request_is_refused_without_any_write(string outbound)
    {
        var h = new Harness(tenant: "tenant-b", stored: Profile(tenant: "tenant-b"));
        TenantConnectorProfile before = h.Profiles.Stored!;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: outbound, inbound: $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("OutboundSettings.sandbox.", result.Message);
        Assert.Contains("referência", result.Message);
        Assert.Equal(0, h.Secrets.SetCount);   // nem o segredo válido da entrada vai para o cofre
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
        Assert.Empty(h.Observer.Tenants);
        Assert.DoesNotContain(Secret, result.Message);
    }

    [Theory]
    [InlineData("""{"sandbox":{"clientSecret":"s3cr3t" """)]
    [InlineData("""["s3cr3t"]""")]
    public async Task Invalid_json_is_refused_without_any_write(string outbound)
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(outbound: outbound, inbound: $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("OutboundSettings", result.Message);
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

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: """{"sandbox":{"token":{"url":"x"}}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("OutboundSettings.sandbox.token", result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
    }

    [Fact]
    public async Task Path_that_does_not_fit_a_vault_name_is_refused()
    {
        var h = new Harness();

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: $$$$"""{"meu_ambiente":{"clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains("OutboundSettings.meu_ambiente.clientSecret", result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.DoesNotContain(Secret, result.Message);
    }

    [Fact]
    public async Task Vault_failure_leaves_the_profile_intact_with_a_message_without_the_value()
    {
        var h = new Harness(stored: Profile());
        TenantConnectorProfile before = h.Profiles.Stored!;
        h.Secrets.FailOnSet = true;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request(outbound: $$$$"""{"sandbox":{"clientSecret":"{{{{Secret}}}}"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.SecretStoreFailed, result.Status);
        Assert.Contains("cofre", result.Message);
        Assert.Contains("OutboundSettings.sandbox.clientSecret", result.Message);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
    }

    [Fact]
    public async Task Observers_are_told_after_the_vault_write_and_the_upsert()
    {
        var h = new Harness();

        await h.Service.SaveAsync(Request(outbound: $$$$"""{"sandbox":{"clientSecret":"{{{{Secret}}}}"}}"""));

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
        var service = new ConnectorProfileService(h.Profiles, h.Secrets, [resolver], new Tenant("tenant-a"));
        TenantConnectorProfile profile = Profile();

        await resolver.GetAsync(profile);
        await service.SaveAsync(Request());
        await resolver.GetAsync(profile);
        await service.SaveAsync(Request());   // a mesma requisição: nada muda, e o aviso vale do mesmo jeito
        await resolver.GetAsync(profile);

        Assert.Equal(3, listing.Calls);
    }

    [Fact]
    public async Task Observers_are_told_even_when_the_upsert_fails_after_a_vault_write()
    {
        var h = new Harness();
        h.Profiles.FailUpsert = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Service.SaveAsync(Request(outbound: $$$$"""{"sandbox":{"clientSecret":"{{{{Secret}}}}"}}""")));

        Assert.Equal(Secret, h.Secrets.Values[SandboxSecretName]);   // o segredo já mudou no cofre
        Assert.Equal(["tenant-a"], h.Observer.Tenants);
    }

    [Fact]
    public async Task Support_fields_absent_from_the_request_keep_the_stored_ones()
    {
        const string support = """{"domain":"acme.freshdesk.com","apiKeyRef":"kv:fh-tenant-a--support--apikey"}""";
        var h = new Harness(stored: Profile() with { SupportAdapter = "Local", SupportSettings = support });

        await h.Service.SaveAsync(Request(outbound: """{"sandbox":{"clientId":"abc"}}"""));

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
        ConnectorProfileRequest request = Request(outbound: $$$$"""{"sandbox":{"clientSecret":"{{{{Secret}}}}"}}""") with
        {
            InboundSettings = $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}""",
            SupportSettings = $$$$"""{"apiKey":"{{{{Secret}}}}"}""",
        };

        string text = request.ToString();

        Assert.DoesNotContain(Secret, text);
        Assert.Contains("Avalara", text);
    }

    // ---- Módulos: apresentação, e não permissão (change module-navigation-and-integration-panel, D2) ----

    [Fact]
    public async Task Save_without_modules_keeps_the_stored_ones()
    {
        var h = new Harness(stored: Profile() with { Modules = ["Fiscal", "Contabil"] });

        await h.Service.SaveAsync(Request(outbound: """{"sandbox":{"clientId":"abc"}}"""));

        Assert.Equal(["Fiscal", "Contabil"], h.Profiles.Stored!.Modules);
    }

    [Fact]
    public async Task Save_with_modules_stores_them_normalized()
    {
        var h = new Harness(stored: Profile());

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(Request() with { Modules = ["Inventario", "Fiscal", "Fiscal"] });

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(["Fiscal", "Inventario"], h.Profiles.Stored!.Modules);
    }

    [Theory]
    [InlineData(new[] { "Fiscal", "Folha" }, "'Folha'")]
    [InlineData(new string[0], "pelo menos um módulo")]
    public async Task Invalid_modules_are_refused_without_any_write(string[] modules, string expected)
    {
        var h = new Harness(stored: Profile() with { Modules = ["Fiscal", "Contabil"] });
        TenantConnectorProfile before = h.Profiles.Stored!;

        ConnectorProfileSaveResult result = await h.Service.SaveAsync(
            Request(inbound: $$$$"""{"auth":{"clientSecret":"{{{{Secret}}}}"}}""") with { Modules = modules });

        Assert.Equal(ConnectorProfileSaveStatus.Invalid, result.Status);
        Assert.Contains(expected, result.Message);
        Assert.Equal(0, h.Secrets.SetCount);
        Assert.Equal(0, h.Profiles.UpsertCount);
        Assert.Same(before, h.Profiles.Stored);
        Assert.Empty(h.Observer.Tenants);
    }

    [Fact]
    public async Task Read_returns_the_modules_and_only_fiscal_when_none_is_stored()
    {
        var withModules = new Harness(stored: Profile() with { Modules = ["Fiscal", "Inventario"] });
        var without = new Harness(stored: Profile());

        Assert.Equal(["Fiscal", "Inventario"], (await withModules.Service.GetAsync())!.Modules);
        Assert.Equal(["Fiscal"], (await without.Service.GetAsync())!.Modules);
    }

    [Fact]
    public void Put_body_with_modules_is_read()
    {
        ConnectorProfileRequest? request = JsonSerializer.Deserialize<ConnectorProfileRequest>("""
            {"environment":"Sandbox","inboundAdapter":"Dynamics365","inboundSettings":"{}",
             "outboundAdapter":"Avalara","outboundSettings":"{}","modules":["Fiscal","Contabil"]}
            """, HostJson);

        Assert.Equal(["Fiscal", "Contabil"], request!.Modules);
    }

    // ---- Leitura ----

    [Fact]
    public async Task Read_returns_the_settings_without_the_references()
    {
        var h = new Harness(stored: Profile(
            outbound: $$$$$"""{"sandbox":{"clientId":"abc","clientSecretRef":"kv:{{{{{SandboxSecretName}}}}}","establishments":{"1":{"codigoEmpresa":"2"}}}}"""));

        ConnectorProfileView? view = await h.Service.GetAsync();

        JsonObject sandbox = Section(view!.OutboundSettings, "sandbox");
        Assert.False(sandbox.ContainsKey("clientSecretRef"));
        Assert.Equal("abc", (string?)sandbox["clientId"]);
        Assert.Equal("2", (string?)sandbox["establishments"]!["1"]!["codigoEmpresa"]);
    }

    [Fact]
    public async Task Secrets_map_says_configured_with_the_date_of_the_current_version()
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"sandbox":{"clientSecretRef":"kv:{{{{SandboxSecretName}}}}"}}"""));
        h.Secrets.Values[SandboxSecretName] = Secret;

        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(new SecretStatus(true, SavedOn), view!.Secrets["outbound.sandbox.clientSecret"]);
        Assert.Equal(0, h.Secrets.GetCount);   // a leitura descreve, e nunca lê o valor
    }

    [Fact]
    public async Task Reference_without_value_in_the_vault_is_not_configured()
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"sandbox":{"clientSecretRef":"kv:{{{{SandboxSecretName}}}}"}}"""));

        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(new SecretStatus(false, null), view!.Secrets["outbound.sandbox.clientSecret"]);
    }

    [Theory]
    [InlineData("kv:fh-tenant-b--outbound--sandbox--clientsecret")]   // de outro tenant
    [InlineData("s3cr3t")]                                           // malformada
    public async Task Foreign_or_malformed_reference_is_not_configured_and_the_vault_is_not_asked(string reference)
    {
        var h = new Harness(stored: Profile(outbound: $$$$"""{"sandbox":{"clientSecretRef":"{{{{reference}}}}"}}"""));
        h.Secrets.Values["fh-tenant-b--outbound--sandbox--clientsecret"] = "do-outro";

        ConnectorProfileView? view = await h.Service.GetAsync();

        Assert.Equal(new SecretStatus(false, null), view!.Secrets["outbound.sandbox.clientSecret"]);
        Assert.Equal(0, h.Secrets.DescribeCount);
    }

    [Fact]
    public async Task Read_never_contains_the_value_part_of_it_or_the_reference()
    {
        var h = new Harness(stored: Profile(
            inbound: $$$$"""{"auth":{"clientId":"app","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"}}""",
            // Segredo em claro gravado por SQL direto: a leitura também não o devolve.
            outbound: $$$$"""{"sandbox":{"clientSecretRef":"kv:{{{{SandboxSecretName}}}}","password":"{{{{Secret}}}}"}}""")
            with { SupportSettings = $$$$"""{"apiKeyRef":"kv:fh-tenant-a--support--apikey"}""" });
        h.Secrets.Values[SandboxSecretName] = Secret;
        h.Secrets.Values["fh-tenant-a--inbound--auth--clientsecret"] = Secret;

        ConnectorProfileView? view = await h.Service.GetAsync();
        string json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain(Secret, json);
        Assert.DoesNotContain(Secret[^4..], json);   // nem os últimos 4
        Assert.DoesNotContain("kv:", json);
        Assert.DoesNotContain("fh-tenant-a", json);
        Assert.Equal(["inbound.auth.clientSecret", "outbound.sandbox.clientSecret", "support.apiKey"], view!.Secrets.Keys.Order());
    }

    [Fact]
    public async Task Read_of_a_tenant_without_profile_is_null()
    {
        var h = new Harness();

        Assert.Null(await h.Service.GetAsync());
    }

    // ---- Apoio ----

    private static ConnectorProfileRequest Request(string? outbound = null, string? inbound = null, string outboundAdapter = "Avalara")
        => new("Sandbox", "Dynamics365", inbound, outboundAdapter, outbound);

    private static ConnectorProfileRequest RequestWith(ConnectorSettingsKind kind, string json) => kind switch
    {
        ConnectorSettingsKind.Inbound => Request(inbound: json),
        ConnectorSettingsKind.Outbound => Request(outbound: json),
        _ => Request() with { SupportAdapter = "Local", SupportSettings = json },
    };

    private static TenantConnectorProfile Profile(string tenant = "tenant-a", string inbound = "{}", string outbound = "{}") => new()
    {
        TenantId = tenant,
        Environment = "Sandbox",
        InboundAdapter = "Dynamics365",
        InboundSettings = inbound,
        OutboundAdapter = "Avalara",
        OutboundSettings = outbound,
    };

    private static string SettingsOf(TenantConnectorProfile profile, ConnectorSettingsKind kind) => kind switch
    {
        ConnectorSettingsKind.Inbound => profile.InboundSettings,
        ConnectorSettingsKind.Outbound => profile.OutboundSettings,
        _ => profile.SupportSettings,
    };

    private static JsonObject Section(string json, string name) => (JsonObject)JsonNode.Parse(json)![name]!;

    private sealed class Harness
    {
        public Harness(string tenant = "tenant-a", TenantConnectorProfile? stored = null)
        {
            Profiles = new FakeProfiles { Stored = stored };
            Observer = new RecordingObserver(Secrets, Profiles);
            Service = new ConnectorProfileService(Profiles, Secrets, [Observer], new Tenant(tenant));
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
