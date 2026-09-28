using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Settings de saída da Avalara (design D4 e D5): os códigos da empresa vêm da tabela de estabelecimentos do ambiente
/// ativo, nunca do ERP; a mesma tabela diz qual parte da nota é a nossa quando a origem não diz. A credencial e as URLs
/// vêm da mesma seção, sem fallback global (ADR-0027). Falta de configuração é rejeição com motivo claro, antes de
/// qualquer requisição.
/// </summary>
public class AvalaraOutboundSettingsTests
{
    private const string Contoso = "44278225000180";
    private const string Proseware = "11613525000119";

    private const string SandboxSecret = "fh-tenant-a--outbound--sandbox--clientsecret";

    private const string Complete = $$"""
        {"sandbox":{"baseUrl":"https://avalara-sandbox/","clientId":"id-sandbox","clientSecretRef":"kv:{{SandboxSecret}}",
                    "establishments":{"{{Contoso}}":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"} } },
         "production":{"baseUrl":"https://avalara-prod/","tokenUrl":"https://login.avalara-prod/connect/token","clientId":"id-prod",
                       "clientSecretRef":"kv:fh-tenant-a--outbound--production--clientsecret",
                       "establishments":{"{{Contoso}}":{"codigoEmpresa":"PROD-EMP","codigoContribuinte":"PROD-CTB"} } } }
        """;

    // ---------- códigos da empresa ----------

    [Fact]
    public void Codes_come_from_the_active_environment_table_and_not_from_the_erp()
    {
        AvalaraCompanyCodes codes = Read(Complete).CodesFor(Contoso);

        Assert.Equal(new AvalaraCompanyCodes("20247332000182", "20247332000182"), codes);
        Assert.NotEqual(Contoso, codes.CodigoEmpresa);
    }

    [Fact]
    public void Production_profile_reads_the_production_table()
        => Assert.Equal(new AvalaraCompanyCodes("PROD-EMP", "PROD-CTB"), Read(Complete, environment: "Production").CodesFor(Contoso));

    [Fact]
    public void Formatted_cnpj_key_matches_by_digits()
    {
        const string settings = """{"sandbox":{"establishments":{"44.278.225/0001-80":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}}""";

        Assert.Equal(new AvalaraCompanyCodes("E", "C"), Read(settings).CodesFor(Contoso));
    }

    [Fact]
    public void Establishment_without_translation_is_rejected_citing_tenant_environment_and_cnpj()
    {
        string reason = Rejection(() => Read(Complete).CodesFor(Proseware));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("tenant-a", reason);
        Assert.Contains("sandbox", reason);
        Assert.Contains(Proseware, reason);
        Assert.Contains("codigoEmpresa", reason);
        Assert.Contains("codigoContribuinte", reason);
    }

    [Theory]
    [InlineData("""{"codigoEmpresa":"E"}""", "codigoContribuinte")]
    [InlineData("""{"codigoContribuinte":"C"}""", "codigoEmpresa")]
    [InlineData("""{"codigoEmpresa":"","codigoContribuinte":"C"}""", "codigoEmpresa")]
    public void Entry_missing_one_code_names_it(string entry, string missing)
    {
        string reason = Rejection(() => Read($$"""{"sandbox":{"establishments":{"{{Contoso}}": {{entry}} } } }""").CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains(missing, reason);
        Assert.Contains(Contoso, reason);
    }

    [Theory]
    [InlineData("""{"production":{"establishments":{}}}""", "sandbox")]                 // sem a seção do ambiente ativo
    [InlineData("""{"sandbox":{"baseUrl":"http://avalara/"}}""", "establishments")]     // sem a tabela
    [InlineData("""{"sandbox": [""", "JSON")]                                           // settings malformadas
    public void Missing_or_broken_configuration_is_rejected_naming_what_is_wrong(string settings, string cited)
    {
        string reason = Rejection(() => Read(settings).CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains(cited, reason);
    }

    [Fact]
    public void Tenant_without_profile_is_rejected()
    {
        string reason = Rejection(() => AvalaraOutboundSettings.Read("tenant-a", null).CodesFor(Contoso));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("perfil", reason);
    }

    // ---------- credencial e URLs (ADR-0027) ----------

    [Fact]
    public void Credential_and_urls_come_from_the_active_section()
    {
        AvalaraOutboundSettings sandbox = Read(Complete);
        AvalaraOutboundSettings production = Read(Complete, environment: "Production");

        Assert.Equal(new AvalaraClientCredential("id-sandbox", SandboxSecret), sandbox.Credential);
        Assert.Equal(new Uri("https://avalara-sandbox/"), sandbox.BaseUri);
        Assert.Equal(new AvalaraClientCredential("id-prod", "fh-tenant-a--outbound--production--clientsecret"), production.Credential);
        Assert.Equal(new Uri("https://avalara-prod/"), production.BaseUri);
        Assert.Equal(new Uri("https://login.avalara-prod/connect/token"), production.TokenEndpoint("oauth/token"));
        Assert.Equal("tenant-a", sandbox.TenantId);
        Assert.Equal("sandbox", sandbox.Environment);
    }

    [Theory]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br", "https://api-gateway.sandbox.avalarabrasil.com.br/")]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br/", "https://api-gateway.sandbox.avalarabrasil.com.br/")]
    [InlineData("https://gateway.exemplo/avalara", "https://gateway.exemplo/avalara/")]
    [InlineData("https://gateway.exemplo/avalara/", "https://gateway.exemplo/avalara/")]
    public void Base_url_always_ends_with_a_slash_so_the_paths_are_appended(string baseUrl, string expected)
    {
        AvalaraOutboundSettings settings = Read($$$"""{"sandbox":{"baseUrl":"{{{baseUrl}}}"}}""");

        Assert.Equal(new Uri(expected), settings.BaseUri);
        Assert.EndsWith("/", settings.BaseUri.AbsoluteUri);
        // Sem a barra, "…/avalara" + "taxcompliance/…" viraria "…/taxcompliance/…": o último segmento seria trocado.
        Assert.Equal(new Uri(expected + "taxcompliance/v2/fiscal/dfe"), new Uri(settings.BaseUri, "taxcompliance/v2/fiscal/dfe"));
        Assert.Equal(new Uri(expected + "oauth/token"), settings.TokenEndpoint("oauth/token"));
    }

    [Theory]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token")]
    [InlineData("https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token/")]
    public void Token_url_is_the_full_endpoint_and_is_kept_as_it_is(string tokenUrl)
    {
        AvalaraOutboundSettings settings = Read($$$"""{"sandbox":{"baseUrl":"https://api-gateway.sandbox.avalarabrasil.com.br","tokenUrl":"{{{tokenUrl}}}"}}""");

        Assert.Equal(tokenUrl, settings.TokenEndpoint("oauth/token").AbsoluteUri);   // nenhuma barra acrescentada ou tirada
    }

    [Fact]
    public void Without_token_url_the_endpoint_is_the_base_url_plus_the_token_path()
        => Assert.Equal(new Uri("https://avalara-sandbox/oauth/token"), Read(Complete).TokenEndpoint("oauth/token"));

    [Fact]
    public void Missing_client_id_and_secret_are_named_together_pointing_to_the_screen()
    {
        string reason = Rejection(() => _ = Read("""{"sandbox":{"baseUrl":"https://avalara/"}}""").Credential);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("'tenant-a'", reason);
        Assert.Contains("'sandbox'", reason);
        Assert.Contains("clientId", reason);
        Assert.Contains("Client Secret", reason);
        Assert.Contains("OutboundSettings.sandbox", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox", reason);
    }

    [Fact]
    public void Missing_secret_names_the_field_the_tenant_the_environment_and_the_screen()
    {
        string reason = Rejection(() => _ = Read("""{"sandbox":{"baseUrl":"https://avalara/","clientId":"abc"}}""").Credential);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("OutboundSettings.sandbox.clientSecret", reason);
        Assert.Contains("'tenant-a'", reason);
        Assert.Contains("'sandbox'", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → Client Secret", reason);
    }

    [Fact]
    public void Missing_client_id_names_it()
    {
        string reason = Rejection(() => _ = Read("""{"production":{"baseUrl":"https://avalara/","clientSecretRef":"kv:fh-tenant-a--outbound--production--clientsecret"}}""", environment: "Production").Credential);

        Assert.Contains("OutboundSettings.production.clientId", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Produção → Client ID", reason);
    }

    [Theory]
    [InlineData("""{"sandbox":{"clientId":"abc"}}""")]
    [InlineData("""{"sandbox":{"baseUrl":"","clientId":"abc"}}""")]
    public void Missing_base_url_is_rejected_without_fallback(string settings)
    {
        string reason = Rejection(() => _ = Read(settings).BaseUri);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("OutboundSettings.sandbox.baseUrl", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → URL base", reason);
    }

    [Theory]
    [InlineData("""{"sandbox":{"baseUrl":"http://sandbox.exemplo.com/"}}""", "baseUrl")]
    [InlineData("""{"sandbox":{"baseUrl":"https://avalara/","tokenUrl":"http://login.exemplo.com/token"}}""", "tokenUrl")]
    [InlineData("""{"sandbox":{"baseUrl":"ftp://avalara/"}}""", "baseUrl")]
    [InlineData("""{"sandbox":{"baseUrl":"avalara/"}}""", "baseUrl")]
    public void Url_without_tls_outside_loopback_is_rejected(string settings, string field)
    {
        string reason = Rejection(() => _ = Read(settings).TokenEndpoint("oauth/token"));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains($"OutboundSettings.sandbox.{field}", reason);
        Assert.Contains("https", reason);
    }

    [Theory]
    [InlineData("http://localhost:5100/")]
    [InlineData("http://127.0.0.1:5100/")]
    [InlineData("http://[::1]:5100/")]
    public void Loopback_is_accepted_over_http(string baseUrl)
    {
        AvalaraOutboundSettings settings = Read($$$"""{"sandbox":{"baseUrl":"{{{baseUrl}}}"}}""");

        Assert.Equal(new Uri(baseUrl), settings.BaseUri);
        Assert.Equal(new Uri(new Uri(baseUrl), "oauth/token"), settings.TokenEndpoint("oauth/token"));
    }

    [Theory]
    [InlineData("""{"sandbox":{"baseUrl":"https://avalara/","clientId":"abc","clientSecret":"s3cr3t"}}""", "OutboundSettings.sandbox.clientSecret")]
    [InlineData("""{"sandbox":{"baseUrl":"https://avalara/","clientId":"abc","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret","extra":{"api_key":"s3cr3t"}}}""", "OutboundSettings.sandbox.extra.api_key")]
    public void Persisted_write_field_with_value_is_refused_without_the_value(string settings, string field)
    {
        AvalaraOutboundSettings read = Read(settings);

        foreach (Action use in new Action[] { () => _ = read.Credential, () => _ = read.BaseUri, () => _ = read.TokenEndpoint("oauth/token") })
        {
            string reason = Rejection(use);

            Assert.StartsWith("Configuração do conector:", reason);
            Assert.Contains(field, reason);
            Assert.DoesNotContain("s3cr3t", reason);
        }
    }

    [Theory]
    [InlineData("s3cr3t")]                                            // o valor no lugar da referência
    [InlineData("kv:nome com espaço")]
    [InlineData("kv:fh-tenant-b--outbound--sandbox--clientsecret")]   // do prefixo de outro tenant
    [InlineData("kv:fh-tenant--outbound--sandbox--clientsecret")]     // tenant "tenant": prefixo parecido, outro dono
    [InlineData("kv:avalara-a-sandbox-secret")]                       // o formato antigo do seed
    public void Malformed_or_foreign_reference_is_refused_without_repeating_it(string reference)
    {
        AvalaraOutboundSettings read = Read($$$"""{"sandbox":{"baseUrl":"https://avalara/","clientId":"abc","clientSecretRef":"{{{reference}}}"}}""");

        string reason = Rejection(() => _ = read.Credential);

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains("OutboundSettings.sandbox.clientSecretRef", reason);
        Assert.DoesNotContain(reference, reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → Client Secret", reason);
    }

    [Fact]
    public void Client_token_reference_is_ignored()
    {
        AvalaraOutboundSettings read = Read($$$"""{"sandbox":{"baseUrl":"https://avalara/","clientId":"abc","clientSecretRef":"kv:{{{SandboxSecret}}}","clientTokenRef":"kv:avalara-a-sandbox-token"}}""");

        Assert.Equal(new AvalaraClientCredential("abc", SandboxSecret), read.Credential);
        Assert.Equal(new Uri("https://avalara/"), read.BaseUri);
    }

    [Fact]
    public void Credential_and_urls_of_a_broken_profile_are_rejections()
    {
        Assert.StartsWith("Configuração do conector:", Rejection(() => _ = Read("""{ broken""").BaseUri));
        Assert.Contains("perfil", Rejection(() => _ = AvalaraOutboundSettings.Read("tenant-a", null).Credential));
        Assert.Contains("'sandbox'", Rejection(() => _ = Read("""{"production":{}}""").BaseUri));
    }

    [Fact]
    public void Missing_secret_with_the_vault_empty_says_so()
    {
        string reason = Read(Complete).SecretNotConfigured(vaultLacksValue: true).Reason;

        Assert.Contains("OutboundSettings.sandbox.clientSecret", reason);
        Assert.Contains("o cofre não tem o valor", reason);
        Assert.Contains("Configurações → Conectores → Avalara → Sandbox → Client Secret", reason);
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
        const string settings = """{"sandbox":{"establishments":{"12345678000190":{"codigoEmpresa":"E","codigoContribuinte":"C"}}}}""";

        (Party own, Party partner) = Read(settings).PartiesOf(Invoice(issuer, recipient));

        Assert.Equal(expectedOwn, own.TaxId);
        Assert.NotEqual(expectedOwn, partner.TaxId);
    }

    [Fact]
    public void Without_issuance_and_both_sides_in_the_table_is_rejected_citing_both()
    {
        const string settings = """
            {"sandbox":{"establishments":{"12345678000190":{"codigoEmpresa":"E","codigoContribuinte":"C"},
                                          "12345678000271":{"codigoEmpresa":"E","codigoContribuinte":"C2"}}}}
            """;

        string reason = Rejection(() => Read(settings).PartiesOf(Invoice("12345678000190", "12345678000271")));

        Assert.Contains("12345678000190", reason);
        Assert.Contains("12345678000271", reason);
    }

    [Fact]
    public void Without_issuance_and_no_side_in_the_table_is_rejected_citing_both()
    {
        string reason = Rejection(() => Read(Complete).PartiesOf(Invoice(Proseware, "72458488000106")));

        Assert.StartsWith("Configuração do conector:", reason);
        Assert.Contains(Proseware, reason);
        Assert.Contains("72458488000106", reason);
    }

    private static AvalaraOutboundSettings Read(string outboundSettings, string environment = "Sandbox")
        => AvalaraOutboundSettings.Read("tenant-a", new TenantConnectorProfile
        {
            TenantId = "tenant-a",
            Environment = environment,
            InboundAdapter = "Dynamics365",
            OutboundAdapter = "Avalara",
            OutboundSettings = outboundSettings,
        });

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
