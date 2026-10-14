using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// A configuração da Avalara no perfil (design D4 e D5): os códigos da empresa vêm da tabela de estabelecimentos, nunca do
/// ERP; a mesma tabela diz qual parte da nota é a nossa quando a origem não diz. Desde a change
/// platform-establishment-resolution (D3), a tabela é sobreposição: ausente, ela é vazia, e a plataforma completa. A
/// credencial e as URLs vêm do perfil, sem fallback global (ADR-0027). Falta de configuração é rejeição com motivo claro,
/// antes de qualquer requisição. Desde a change platform-account-and-connectors, a credencial e o endpoint de token são da
/// conta de plataforma, e o endpoint é obrigatório, sem derivação (D11); a URL base por versão e a tabela são do conector
/// Avalara, achado pelo nome (D9); e as settings têm um nível só: o ambiente é o da instância, só contexto dos motivos, e
/// uma seção por ambiente é recusada (D6).
/// </summary>
public class AvalaraOutboundSettingsTests
{
    private const string Contoso = "44278225000180";
    private const string Proseware = "11613525000119";

    private const string Secret = "fh-tenant-a--platform--avalara--clientsecret";
    private const string SecretRef = "kv:" + Secret;
    private const string TokenUrl = "https://avalara-sandbox/oauth/token";

    private const string Complete = $$"""
        {"baseUrls":{"v2":"https://avalara-sandbox/"},
         "establishments":{"{{Contoso}}":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"} } }
        """;

    // ---------- o ambiente é da instância ----------

    [Theory]
    [InlineData("sandbox")]
    [InlineData("production")]
    [InlineData("Production")]
    public void Connector_settings_with_an_environment_section_are_refused_naming_it(string section)
    {
        // Só chega aqui por SQL direto: a gravação pela tela já recusa a seção, e a migração as achatou.
        AvalaraOutboundSettings read = Read($$$"""{"{{{section}}}":{"baseUrls":{"v2":"https://avalara/"} } }""");

        foreach (Action use in new Action[] { () => _ = read.BaseUri, () => read.ThrowIfUnreadable() })
        {
            string reason = Rejection(use);

            Assert.StartsWith("Configuração do conector:", reason);
            Assert.Contains($"'{section}'", reason);
            Assert.Contains("nível só", reason);
            Assert.Contains("'Sandbox'", reason);   // o ambiente da instância, como contexto
        }

        Assert.Equal(new AvalaraClientCredential("id-sandbox", Secret), read.Credential);   // a credencial é da conta
    }

    [Fact]
    public void The_instance_environment_does_not_choose_a_table_or_a_credential()
    {
        AvalaraOutboundSettings sandbox = Read(Complete, environment: "Sandbox");
        AvalaraOutboundSettings production = Read(Complete, environment: "Production");

        Assert.Equal(sandbox.CodesFor(Contoso), production.CodesFor(Contoso));
        Assert.Equal(sandbox.Credential, production.Credential);
        Assert.Equal(sandbox.BaseUri, production.BaseUri);
        Assert.Equal(sandbox.TokenEndpoint, production.TokenEndpoint);
        Assert.Equal("Sandbox", sandbox.Environment);
        Assert.Equal("Production", production.Environment);
    }

    // ---------- a conta e o conector (change platform-account-and-connectors) ----------

    [Fact]
    public void Without_the_platform_account_everything_is_rejected_naming_it()
    {
        AvalaraOutboundSettings read = ReadProfile(new TenantConnectorProfile { TenantId = "tenant-a", InboundAdapter = "Dynamics365" });

        foreach (Action use in new Action[]
        {
            () => _ = read.Credential, () => _ = read.TokenEndpoint, () => _ = read.BaseUri, () => read.ThrowIfUnreadable(),
        })
        {
            string reason = Rejection(use);

            Assert.StartsWith("Configuração do conector:", reason);
            Assert.Contains("não tem a conta de plataforma 'Avalara'", reason);
            Assert.Contains("Configurações → Conectores → Avalara", reason);
        }
    }

    [Fact]
    public void Without_the_connector_the_base_and_the_table_are_rejected_and_the_account_still_reads()
    {
        PlatformAccount account = Account(Complete) with { Connectors = [] };
        AvalaraOutboundSettings read = ReadProfile(Profile(account));

        foreach (Action use in new Action[] { () => _ = read.BaseUri, () => read.ThrowIfUnreadable(), () => read.CodesFor(Contoso) })
        {
            Assert.Contains("não tem o conector 'Avalara'", Rejection(use));
        }

        Assert.Equal(new AvalaraClientCredential("id-sandbox", Secret), read.Credential);
        Assert.Equal(new Uri(TokenUrl), read.TokenEndpoint);
    }

    [Fact]
    public void The_table_and_the_base_are_read_from_the_connector_of_the_adapter()
    {
        // Outro conector sob a mesma conta, com outra base e outra tabela: o adapter lê o seu, pelo nome.
        PlatformAccount account = Account(Complete) with
        {
            Connectors =
            [
                new PlatformConnector
                {
                    Adapter = "Outro",
                    Settings = $$"""{"baseUrls":{"v2":"https://outro/"},"establishments":{"{{Contoso}}":{"codigoEmpresa":"X","codigoContribuinte":"Y"} } }""",
                },
                new PlatformConnector { Adapter = "Avalara", Settings = Complete },
            ],
        };

        AvalaraOutboundSettings read = ReadProfile(Profile(account));

        Assert.Equal(new AvalaraCompanyCodes("20247332000182", "20247332000182"), read.CodesFor(Contoso));
        Assert.Equal(new Uri("https://avalara-sandbox/"), read.BaseUri);
    }

    [Theory]
    [InlineData("avalara", "Avalara")]   // o conector com o nome em outra caixa
    [InlineData("Avalara", "AVALARA")]   // a conta com o nome em outra caixa
    public void Account_and_connector_are_found_by_the_exact_name(string connectorAdapter, string platform)
    {
        PlatformAccount account = Account(Complete) with
        {
            Platform = platform,
            Connectors = [new PlatformConnector { Adapter = connectorAdapter, Settings = Complete }],
        };

        AvalaraOutboundSettings read = ReadProfile(Profile(account));

        Assert.StartsWith("Configuração do conector:", Rejection(() => _ = read.BaseUri));
    }

    // ---------- o caminho da integração de notas (change platform-account-and-connectors, 6.8 e 6.12) ----------

    [Fact]
    public void The_submit_goes_to_the_base_of_the_version_plus_the_path_of_the_integration_and_the_version_never_enters_the_url()
    {
        AvalaraOutboundSettings read = ReadWith(AvalaraProfiles.Connector("https://api.exemplo.com/"), "taxcompliance/v2/fiscal/dfe");

        Assert.Equal(new Uri("https://api.exemplo.com/taxcompliance/v2/fiscal/dfe"), read.SubmitUri);
        Assert.Equal(new Uri("https://api.exemplo.com/taxcompliance/v2/fiscal/dfe/abc/status"), read.StatusUri("abc"));
    }

    [Theory]
    [InlineData("/taxcompliance/v2/fiscal/dfe")]    // a barra do começo não leva o caminho para a raiz do host
    [InlineData("taxcompliance/v2/fiscal/dfe/")]    // a do fim não dobra na consulta
    public void A_slash_at_either_end_of_the_path_does_not_change_the_urls(string path)
    {
        AvalaraOutboundSettings read = ReadWith(AvalaraProfiles.Connector("https://gateway.exemplo/avalara/"), path);

        Assert.StartsWith("https://gateway.exemplo/avalara/taxcompliance/v2/fiscal/dfe", read.SubmitUri.AbsoluteUri);
        Assert.Equal(new Uri("https://gateway.exemplo/avalara/taxcompliance/v2/fiscal/dfe/abc/status"), read.StatusUri("abc"));
    }

    [Fact]
    public void Without_the_invoices_integration_the_submit_is_a_connector_rejection_naming_it_and_the_screen()
    {
        AvalaraOutboundSettings read = ReadWith(Complete, invoicesPath: null);

        foreach (Action use in new Action[] { () => _ = read.SubmitUri, () => read.StatusUri("abc") })
        {
            string reason = Rejection(use);

            Assert.StartsWith("Configuração do conector:", reason);
            Assert.Contains("integração de notas fiscais (notas-fiscais)", reason);
            Assert.Contains("Configurações → Conectores → Avalara", reason);
        }
    }

    [Fact]
    public void With_fiscal_off_the_submit_is_a_connector_rejection_naming_the_invoices_integration()
    {
        // Desligar o módulo desliga as integrações dele (module-navigation, 6.12): o envio de notas para.
        PlatformAccount account = Account(Complete) with
        {
            Connectors = [new PlatformConnector { Adapter = "Avalara", Settings = Complete, Modules = [new ConnectorModule { Module = TenantModules.Inventory }] }],
        };

        string reason = Rejection(() => _ = ReadProfile(Profile(account)).SubmitUri);

        Assert.Contains("integração de notas fiscais (notas-fiscais)", reason);
        Assert.Contains("módulo Fiscal", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]   // à espera do host, que ainda não subiu depois da migração
    public void An_invoices_integration_without_a_path_is_inactive_and_the_submit_a_connector_rejection(string? path)
    {
        PlatformAccount account = Account(Complete) with
        {
            Connectors =
            [
                new PlatformConnector
                {
                    Adapter = "Avalara",
                    Settings = Complete,
                    Modules = [new ConnectorModule
                    {
                        Module = TenantModules.Fiscal,
                        Integrations = [new ConnectorIntegration { Key = ConnectorIntegrations.Invoices, Endpoint = path }],
                    }],
                },
            ],
        };

        string reason = Rejection(() => _ = ReadProfile(Profile(account)).SubmitUri);

        Assert.Contains("está sem caminho, e fica inativa", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Notas fiscais", reason);
    }

    // ---------- códigos da empresa ----------

    [Fact]
    public void Codes_come_from_the_table_and_not_from_the_erp()
    {
        AvalaraCompanyCodes codes = Read(Complete).CodesFor(Contoso);

        Assert.Equal(new AvalaraCompanyCodes("20247332000182", "20247332000182"), codes);
        Assert.NotEqual(Contoso, codes.CodigoEmpresa);
    }

    [Fact]
    public void Formatted_cnpj_key_matches_without_the_punctuation()
    {
        const string settings = """{"establishments":{"44.278.225/0001-80":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""";

        Assert.Equal(new AvalaraCompanyCodes("E", "C"), Read(settings).CodesFor(Contoso));
    }

    [Fact]
    public void Alphanumeric_cnpj_key_matches_with_its_letters()
    {
        const string settings = """{"establishments":{"12.ABC.345/01DE-35":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""";

        Assert.Equal(new AvalaraCompanyCodes("E", "C"), Read(settings).CodesFor("12ABC34501DE35"));
    }

    [Fact]
    public void Two_alphanumeric_cnpjs_with_the_same_digits_do_not_share_the_codes()
    {
        // Pela regra de "só dígitos", os dois virariam 123450135, e a nota do segundo levaria os códigos do primeiro.
        const string settings = """{"establishments":{"12ABC34501DE35":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""";

        string reason = Rejection(() => Read(settings).CodesFor("12XYZ34501DE35"));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("12XYZ34501DE35", reason);
    }

    [Fact]
    public void Establishment_without_translation_is_rejected_citing_tenant_environment_and_cnpj()
    {
        string reason = Rejection(() => Read(Complete).CodesFor(Proseware));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("tenant-a", reason);
        Assert.Contains("'Sandbox' da instância", reason);
        Assert.Contains(Proseware, reason);
        Assert.Contains("codigoEmpresa", reason);
        Assert.Contains("codigoContribuinte", reason);
        Assert.Contains("na tabela establishments do conector 'Avalara'", reason);
    }

    [Theory]
    [InlineData("""{"codigoEmpresa":"E"}""", "codigoContribuinte")]
    [InlineData("""{"codigoContribuinte":"C"}""", "codigoEmpresa")]
    [InlineData("""{"codigoEmpresa":"","codigoContribuinte":"C"}""", "codigoEmpresa")]
    public void Entry_missing_one_code_names_it(string entry, string missing)
    {
        string reason = Rejection(() => Read($$"""{"establishments":{"{{Contoso}}": {{entry}} } }""").CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains(missing, reason);
        Assert.Contains(Contoso, reason);
        Assert.Contains($"na tabela establishments do conector 'Avalara' (entrada \"{Contoso}\")", reason);
    }

    // A seção sem a tabela saiu desta lista com a change platform-establishment-resolution (D3): ela deixou de ser problema
    // por si só, e é a sobreposição vazia (os testes logo abaixo).
    [Theory]
    [InlineData("""{"production":{"establishments":{}}}""", "'production'")]                           // seção por ambiente
    [InlineData("""{"establishments": [""", "JSON")]                                                     // settings malformadas
    [InlineData("""["não é objeto"]""", "objeto JSON")]                                                    // a raiz não é objeto
    [InlineData("""{"establishments":["44278225000180"]}""", "a tabela establishments do conector")]    // a tabela não é objeto
    public void Missing_or_broken_configuration_is_rejected_naming_what_is_wrong(string settings, string cited)
    {
        string reason = Rejection(() => Read(settings).CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains(cited, reason);
    }

    [Fact]
    public void Settings_without_the_table_are_an_empty_overlay_and_not_a_problem()
    {
        AvalaraOutboundSettings settings = Read(AvalaraProfiles.Connector("http://avalara/"));

        Assert.Null(settings.OverrideFor(Contoso));
        Assert.False(settings.HasOverride(Contoso));
        settings.ThrowIfUnreadable();
    }

    [Fact]
    public async Task Without_the_table_the_platform_gives_the_codes()
    {
        PlatformEstablishmentIndex index = await Indexes.Of(new PlatformEstablishment(Contoso, "012", "010", "2000010001", "METALURGICA"));

        AvalaraCompanyCodes codes = Read(AvalaraProfiles.Connector("http://avalara/")).CodesFor(Contoso, index);

        Assert.Equal(new AvalaraCompanyCodes("012", "010"), codes);
    }

    [Fact]
    public void Without_the_table_and_a_destination_that_does_not_list_it_is_the_missing_translation()
    {
        string reason = Rejection(() => Read(AvalaraProfiles.Connector("http://avalara/")).CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("não tem tradução", reason);
        Assert.Contains("na tabela establishments do conector 'Avalara'", reason);
    }

    [Fact]
    public async Task The_entry_wins_over_the_platform()
    {
        PlatformEstablishmentIndex index = await Indexes.Of(new PlatformEstablishment(Contoso, "012", "010", "2000010001", "METALURGICA"));

        Assert.Equal(new AvalaraCompanyCodes("20247332000182", "20247332000182"), Read(Complete).CodesFor(Contoso, index));
    }

    [Fact]
    public async Task An_incomplete_entry_does_not_fall_back_to_the_platform()
    {
        PlatformEstablishmentIndex index = await Indexes.Of(new PlatformEstablishment(Contoso, "012", "010", "2000010001", "METALURGICA"));

        string reason = Rejection(() => Read($$"""{"establishments":{"{{Contoso}}":{"codigoEmpresa":"E"} } }""").CodesFor(Contoso, index));

        Assert.Contains("codigoContribuinte", reason);
        Assert.DoesNotContain("010", reason);
    }

    [Fact]
    public void Tenant_without_profile_is_rejected()
    {
        string reason = Rejection(() => AvalaraOutboundSettings.Read("tenant-a", null, Sandbox).CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("perfil", reason);
    }

    // ---------- credencial e URLs (ADR-0027) ----------

    [Fact]
    public void Credential_and_token_endpoint_come_from_the_account_and_the_base_from_the_connector()
    {
        AvalaraOutboundSettings read = Read(Complete);

        Assert.Equal(new AvalaraClientCredential("id-sandbox", Secret), read.Credential);
        Assert.Equal(new Uri(TokenUrl), read.TokenEndpoint);
        Assert.Equal(new Uri("https://avalara-sandbox/"), read.BaseUri);
        Assert.Equal("tenant-a", read.TenantId);
        Assert.Equal("Sandbox", read.Environment);
        Assert.Equal("Avalara", read.Platform);
    }

    [Theory]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br", "https://api-gateway.sandbox.avalarabrasil.com.br/")]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br/", "https://api-gateway.sandbox.avalarabrasil.com.br/")]
    [InlineData("https://gateway.exemplo/avalara", "https://gateway.exemplo/avalara/")]
    [InlineData("https://gateway.exemplo/avalara/", "https://gateway.exemplo/avalara/")]
    public void Base_url_always_ends_with_a_slash_so_the_paths_are_appended(string baseUrl, string expected)
    {
        AvalaraOutboundSettings settings = Read(AvalaraProfiles.Connector(baseUrl));

        Assert.Equal(new Uri(expected), settings.BaseUri);
        Assert.EndsWith("/", settings.BaseUri.AbsoluteUri);
        // Sem a barra, "…/avalara" + "taxcompliance/…" viraria "…/taxcompliance/…": o último segmento seria trocado.
        Assert.Equal(new Uri(expected + "taxcompliance/v2/fiscal/dfe"), new Uri(settings.BaseUri, "taxcompliance/v2/fiscal/dfe"));
    }

    [Fact]
    public void The_version_key_chooses_the_base_and_never_enters_the_url()
    {
        AvalaraOutboundSettings settings = Read("""{"baseUrls":{"v1":"https://v1.avalara/","v2":"https://v2.avalara/"}}""");

        Assert.Equal(new Uri("https://v2.avalara/"), settings.BaseUri);   // a v2 é a do envio fiscal, e a URL é a cadastrada
    }

    [Theory]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token")]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token/")]
    public void Token_url_is_the_full_endpoint_and_is_kept_as_it_is(string tokenUrl)
    {
        AvalaraOutboundSettings settings = Read(AvalaraProfiles.Connector("https://api-gateway.sandbox.avalarabrasil.com.br"), tokenUrl: tokenUrl);

        Assert.Equal(tokenUrl, settings.TokenEndpoint.AbsoluteUri);   // nenhuma barra acrescentada ou tirada
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Account_without_token_url_is_rejected_naming_the_field_and_nothing_is_derived(string? tokenUrl)
    {
        // Antes, sem tokenUrl, o endpoint era a URL base mais o Avalara:TokenPath. Agora ele é da conta, e obrigatório (D11).
        AvalaraOutboundSettings read = Read(Complete, tokenUrl: tokenUrl);

        string reason = Rejection(() => _ = read.TokenEndpoint);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("tokenUrl", reason);
        Assert.Contains("conta de plataforma 'Avalara'", reason);
        Assert.Contains("não o deriva da URL base", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Endpoint de token", reason);
        Assert.Equal(new Uri("https://avalara-sandbox/"), read.BaseUri);   // a base do conector continua lida, e não vira token
    }

    [Fact]
    public void Missing_client_id_and_secret_are_named_together_pointing_to_the_screen()
    {
        string reason = Rejection(() => _ = Read(Complete, clientId: null, clientSecretRef: null).Credential);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("'tenant-a'", reason);
        Assert.Contains("'Sandbox' da instância", reason);
        Assert.Contains("clientId", reason);
        Assert.Contains("Client Secret", reason);
        Assert.Contains("na conta de plataforma 'Avalara'", reason);
        Assert.Contains("Configurações → Conectores → Avalara", reason);
        Assert.DoesNotContain("→ Sandbox", reason);   // a tela não tem mais um bloco por ambiente
    }

    [Fact]
    public void Missing_secret_names_the_field_the_tenant_the_environment_and_the_screen()
    {
        string reason = Rejection(() => _ = Read(Complete, clientSecretRef: null).Credential);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("o Client Secret da conta de plataforma 'Avalara'", reason);
        Assert.Contains("'tenant-a'", reason);
        Assert.Contains("'Sandbox' da instância", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Client Secret", reason);
    }

    [Fact]
    public void Missing_client_id_names_it()
    {
        string reason = Rejection(() => _ = Read(Complete, environment: "Production", clientId: null).Credential);

        Assert.Contains("clientId", reason);
        Assert.Contains("na conta de plataforma 'Avalara'", reason);
        Assert.Contains("'Production' da instância", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Client ID", reason);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"baseUrls":{"v2":""}}""")]
    [InlineData("""{"baseUrls":{"v1":"https://avalara/"}}""")]   // a base de outra versão não serve ao envio
    [InlineData("""{"baseUrl":"https://avalara/"}""")]           // a forma achatada antiga não é lida
    public void Missing_base_url_of_the_version_is_rejected_without_fallback(string settings)
    {
        string reason = Rejection(() => _ = Read(settings).BaseUri);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("baseUrls.v2", reason);
        Assert.Contains("Configurações → Conectores → Avalara → URL base (v2)", reason);
    }

    [Theory]
    [InlineData("http://sandbox.exemplo.com/")]
    [InlineData("ftp://avalara/")]
    [InlineData("avalara/")]
    public void Base_url_without_tls_outside_loopback_is_rejected(string baseUrl)
    {
        string reason = Rejection(() => _ = Read(AvalaraProfiles.Connector(baseUrl)).BaseUri);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("baseUrls.v2", reason);
        Assert.Contains("https", reason);
    }

    [Fact]
    public void Token_url_without_tls_outside_loopback_is_rejected()
    {
        string reason = Rejection(() => _ = Read(Complete, tokenUrl: "http://login.exemplo.com/token").TokenEndpoint);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("tokenUrl", reason);
        Assert.Contains("https", reason);
    }

    [Theory]
    [InlineData("http://localhost:5100/")]
    [InlineData("http://127.0.0.1:5100/")]
    [InlineData("http://[::1]:5100/")]
    public void Loopback_is_accepted_over_http(string baseUrl)
    {
        AvalaraOutboundSettings settings = Read(AvalaraProfiles.Connector(baseUrl), tokenUrl: baseUrl + "oauth/token");

        Assert.Equal(new Uri(baseUrl), settings.BaseUri);
        Assert.Equal(new Uri(baseUrl + "oauth/token"), settings.TokenEndpoint);
    }

    [Theory]
    [InlineData("""{"baseUrls":{"v2":"https://avalara/"},"apiKey":"s3cr3t"}""", "apiKey")]
    [InlineData("""{"baseUrls":{"v2":"https://avalara/"},"extra":{"api_key":"s3cr3t"}}""", "extra.api_key")]
    public void Persisted_write_field_with_value_in_the_connector_is_refused_without_the_value(string settings, string field)
    {
        AvalaraOutboundSettings read = Read(settings);

        foreach (Action use in new Action[] { () => _ = read.Credential, () => _ = read.BaseUri, () => _ = read.TokenEndpoint })
        {
            string reason = Rejection(use);

            Assert.StartsWith("Configuração do conector:", reason);
            Assert.Contains($"o segredo {field} está em claro", reason);
            Assert.DoesNotContain("s3cr3t", reason);
        }
    }

    [Theory]
    [InlineData("s3cr3t")]                                                     // o valor no lugar da referência
    [InlineData("kv:nome com espaço")]
    [InlineData("kv:fh-tenant-b--platform--avalara--clientsecret")]            // do prefixo de outro tenant
    [InlineData("kv:fh-tenant--platform--avalara--clientsecret")]              // tenant "tenant": prefixo parecido, outro dono
    [InlineData("kv:avalara-a-sandbox-secret")]                                // o formato antigo do seed
    public void Malformed_or_foreign_reference_is_refused_without_repeating_it(string reference)
    {
        AvalaraOutboundSettings read = Read(Complete, clientSecretRef: reference);

        string reason = Rejection(() => _ = read.Credential);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("clientSecretRef", reason);
        Assert.DoesNotContain(reference, reason);
        Assert.Contains("Configurações → Conectores → Avalara → Client Secret", reason);
    }

    [Fact]
    public void Client_token_reference_is_ignored()
    {
        AvalaraOutboundSettings read = Read(AvalaraProfiles.Connector("https://avalara/", ""","clientTokenRef":"kv:avalara-a-sandbox-token" """));

        Assert.Equal(new AvalaraClientCredential("id-sandbox", Secret), read.Credential);
        Assert.Equal(new Uri("https://avalara/"), read.BaseUri);
    }

    [Fact]
    public void Credential_and_urls_of_a_broken_profile_are_rejections()
    {
        Assert.StartsWith("Configuração do conector:", Rejection(() => _ = Read("""{ broken""").BaseUri));
        Assert.Contains("perfil", Rejection(() => _ = AvalaraOutboundSettings.Read("tenant-a", null, Sandbox).Credential));
        Assert.Contains("perfil", Rejection(() => _ = AvalaraOutboundSettings.Read("tenant-a", null, Sandbox).TokenEndpoint));
        Assert.Contains("'production'", Rejection(() => _ = Read("""{"production":{}}""").BaseUri));
    }

    [Fact]
    public void Missing_secret_with_the_vault_empty_says_so()
    {
        string reason = Read(Complete).SecretNotConfigured(vaultLacksValue: true).Reason;

        Assert.Contains("o Client Secret da conta de plataforma 'Avalara'", reason);
        Assert.Contains("o cofre não tem o valor", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Client Secret", reason);
    }

    // ---------- estabelecimento próprio e parceiro ----------

    [Fact]
    public void Own_issuance_makes_the_issuer_ours_and_the_recipient_the_partner()
    {
        GoodsInvoice invoice = Invoice(issuer: Contoso, recipient: "72458488000106") with { Issuance = Issuance.Own };

        (Party own, Party partner) = Read(Complete).PartiesOf(invoice);

        Assert.Equal(Contoso, own.TaxId);
        Assert.Equal("72458488000106", partner.TaxId);
    }

    [Fact]
    public void Third_party_issuance_makes_the_recipient_ours_and_the_issuer_the_partner()
    {
        GoodsInvoice invoice = Invoice(issuer: Proseware, recipient: Contoso) with { Issuance = Issuance.ThirdParty };

        (Party own, Party partner) = Read(Complete).PartiesOf(invoice);

        Assert.Equal(Contoso, own.TaxId);
        Assert.Equal(Proseware, partner.TaxId);
    }

    [Theory]
    [InlineData("12345678000190", "98765432000110", "12345678000190")]   // nfe-exemplo.xml: o tenant emite
    [InlineData("98765432000188", "12345678000190", "12345678000190")]   // nfe-exemplo-2.xml: o tenant recebe
    public void Without_issuance_the_table_says_which_side_is_ours(string issuer, string recipient, string expectedOwn)
    {
        const string settings = """{"establishments":{"12345678000190":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""";

        (Party own, Party partner) = Read(settings).PartiesOf(Invoice(issuer, recipient));

        Assert.Equal(expectedOwn, own.TaxId);
        Assert.NotEqual(expectedOwn, partner.TaxId);
    }

    [Fact]
    public void Without_issuance_the_table_of_the_connector_says_which_side_is_ours()
    {
        // A regra do estabelecimento próprio lê a tabela do conector Avalara, e não a de outro conector da conta (4.9).
        PlatformAccount account = Account(AvalaraProfiles.Connector("https://avalara/")) with
        {
            Connectors =
            [
                new PlatformConnector { Adapter = "Outro", Settings = """{"establishments":{"98765432000110":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""" },
                new PlatformConnector { Adapter = "Avalara", Settings = """{"establishments":{"12345678000190":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""" },
            ],
        };

        (Party own, Party partner) = ReadProfile(Profile(account)).PartiesOf(Invoice("12345678000190", "98765432000110"));

        Assert.Equal("12345678000190", own.TaxId);
        Assert.Equal("98765432000110", partner.TaxId);
    }

    [Fact]
    public void Without_issuance_an_alphanumeric_cnpj_in_the_table_is_ours()
    {
        const string settings = """{"establishments":{"12.ABC.345/01DE-35":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}""";

        (Party own, Party partner) = Read(settings).PartiesOf(Invoice("72458488000106", "12ABC34501DE35"));

        Assert.Equal("12ABC34501DE35", own.TaxId);
        Assert.Equal("72458488000106", partner.TaxId);
    }

    [Fact]
    public void Without_issuance_and_both_sides_in_the_table_is_rejected_citing_both()
    {
        const string settings = """
            {"establishments":{"12345678000190":{"codigoEmpresa":"E","codigoContribuinte":"C"},
                               "12345678000271":{"codigoEmpresa":"E","codigoContribuinte":"C2"}}}
            """;

        string reason = Rejection(() => Read(settings).PartiesOf(Invoice("12345678000190", "12345678000271")));

        Assert.Contains("12345678000190", reason);
        Assert.Contains("12345678000271", reason);
        Assert.Contains("na tabela establishments do conector 'Avalara'", reason);
    }

    [Fact]
    public void Without_issuance_and_no_side_in_the_table_is_rejected_citing_both()
    {
        string reason = Rejection(() => Read(Complete).PartiesOf(Invoice(Proseware, "72458488000106")));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains(Proseware, reason);
        Assert.Contains("72458488000106", reason);
    }

    private static readonly IInstanceEnvironment Sandbox = InstanceEnvironment.From("Sandbox");

    // A conta Avalara do tenant-a, com o conector Avalara e as settings dadas, e a integração de notas ligada.
    private static PlatformAccount Account(
        string connectorSettings, string? clientId = "id-sandbox", string? clientSecretRef = SecretRef, string? tokenUrl = TokenUrl,
        string? invoicesPath = AvalaraProfiles.InvoicesPath)
        => new()
        {
            Platform = "Avalara",
            ClientId = clientId,
            ClientSecretRef = clientSecretRef,
            TokenUrl = tokenUrl,
            Connectors = [new PlatformConnector { Adapter = "Avalara", Settings = connectorSettings, Modules = AvalaraProfiles.Invoices(invoicesPath) }],
        };

    private static AvalaraOutboundSettings ReadWith(string connectorSettings, string? invoicesPath)
        => ReadProfile(Profile(Account(connectorSettings, invoicesPath: invoicesPath)));

    private static TenantConnectorProfile Profile(PlatformAccount account) => new()
    {
        TenantId = "tenant-a",
        InboundAdapter = "Dynamics365",
        Accounts = [account],
    };

    private static AvalaraOutboundSettings Read(
        string connectorSettings, string environment = "Sandbox", string? clientId = "id-sandbox", string? clientSecretRef = SecretRef,
        string? tokenUrl = TokenUrl)
        => ReadProfile(Profile(Account(connectorSettings, clientId, clientSecretRef, tokenUrl)), environment);

    private static AvalaraOutboundSettings ReadProfile(TenantConnectorProfile profile, string environment = "Sandbox")
        => AvalaraOutboundSettings.Read("tenant-a", profile, InstanceEnvironment.From(environment));

    private static string Rejection(Action act) => Assert.Throws<DispatchRejectedException>(act).Reason;

    private static GoodsInvoice Invoice(string issuer, string recipient) => new()
    {
        AccessKey = "",
        Model = "55",
        Series = "1",
        Number = "1",
        IssueDate = new DateTimeOffset(2016, 9, 2, 12, 0, 0, TimeSpan.Zero),
        Issuer = new Party { TaxId = issuer, Name = "Emitente" },
        Recipient = new Party { TaxId = recipient, Name = "Destinatário" },
        TotalAmount = 1m,
        Items = [],
    };
}
