using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Settings de saída da Avalara (design D4 e D5): os códigos da empresa vêm da tabela de estabelecimentos do ambiente
/// ativo, nunca do ERP; a mesma tabela diz qual parte da nota é a nossa quando a origem não diz. Falta de configuração
/// é rejeição com motivo claro, antes de qualquer requisição.
/// </summary>
public class AvalaraOutboundSettingsTests
{
    private const string Contoso = "44278225000180";
    private const string Proseware = "11613525000119";

    private const string Complete = $$"""
        {"sandbox":{"baseUrl":"http://avalara-sandbox/","establishments":{"{{Contoso}}":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"} } },
         "production":{"baseUrl":"http://avalara-prod/","establishments":{"{{Contoso}}":{"codigoEmpresa":"PROD-EMP","codigoContribuinte":"PROD-CTB"} } } }
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

    [Fact]
    public void Base_url_comes_from_the_active_environment_and_is_absent_when_the_settings_do_not_have_it()
    {
        Assert.Equal("http://avalara-sandbox/", Read(Complete).BaseUrl);
        Assert.Null(Read("""{"sandbox":{}}""").BaseUrl);
        Assert.Null(Read("""{ broken""").BaseUrl);             // malformadas: o fallback da config continua valendo
        Assert.Null(AvalaraOutboundSettings.Read("tenant-a", null).BaseUrl);
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
            Realtime = true,
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
