using FiscalHub.Application.Connectors;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>Especifica as settings do adapter Dynamics365: padrões, validação e o que é recusado.</summary>
public class D365InboundSettingsTests
{
    [Fact]
    public void Minimal_settings_use_the_defaults()
    {
        D365InboundSettings s = D365InboundSettings.Parse("""{"url":"https://fiscosysdev.operations.dynamics.com","poll":{"enabled":true}}""");

        Assert.Equal(new Uri("https://fiscosysdev.operations.dynamics.com"), s.Url);
        Assert.Equal(500, s.PageSize);
        Assert.Empty(s.Companies);   // todas as empresas
        Assert.Equal(DocumentType.GoodsInvoice55, s.ModelTypes["55"]);
        Assert.Equal(DocumentType.Transport57, s.ModelTypes["57"]);
        Assert.Equal(DocumentType.ServiceNfse, s.ModelTypes["SE"]);
        Assert.Null(s.Auth);
    }

    [Theory]
    [InlineData("""{"url":""}""")]
    [InlineData("""{"url":"fiscosysdev.operations.dynamics.com"}""")]
    [InlineData("""{"url":"/data"}""")]
    [InlineData("{}")]
    public void Missing_or_relative_url_is_a_configuration_error(string json)
    {
        Assert.Throws<ConnectorSettingsException>(() => D365InboundSettings.Parse(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    [InlineData(20_000)]
    public void Page_size_outside_one_to_ten_thousand_is_a_configuration_error(int pageSize)
    {
        Assert.Throws<ConnectorSettingsException>(() =>
            D365InboundSettings.Parse($$"""{"url":"https://erp.example","pageSize":{{pageSize}}}"""));
    }

    [Fact]
    public void Page_size_at_the_limits_is_accepted()
    {
        Assert.Equal(1, D365InboundSettings.Parse("""{"url":"https://erp.example","pageSize":1}""").PageSize);
        Assert.Equal(10_000, D365InboundSettings.Parse("""{"url":"https://erp.example","pageSize":10000}""").PageSize);
    }

    [Theory]
    [InlineData("""{"url":"https://erp.example","auth":{"tenantId":"t","clientId":"c","clientSecretRef":"abc123"}}""")]
    [InlineData("""{"url":"https://erp.example","auth":{"tenantId":"t","clientId":"c","clientSecret":"abc123"}}""")]
    public void Plain_secret_is_refused(string json)
    {
        Assert.Throws<ConnectorSettingsException>(() => D365InboundSettings.Parse(json));
    }

    [Fact]
    public void Complete_auth_by_reference_is_accepted()
    {
        D365InboundSettings s = D365InboundSettings.Parse(
            """{"url":"https://erp.example","auth":{"tenantId":"t","clientId":"c","clientSecretRef":"kv:d365-a-secret"}}""");

        Assert.Equal(("t", "c", "kv:d365-a-secret"), s.Auth!.RequireComplete());
    }

    [Theory]
    [InlineData("""{"url":"https://erp.example","auth":{"clientId":"c","clientSecretRef":"kv:x"}}""")]
    [InlineData("""{"url":"https://erp.example","auth":{"tenantId":"t","clientSecretRef":"kv:x"}}""")]
    [InlineData("""{"url":"https://erp.example","auth":{"tenantId":"t","clientId":"c"}}""")]
    public void Incomplete_auth_is_a_configuration_error_for_client_credentials(string json)
    {
        D365InboundSettings s = D365InboundSettings.Parse(json);   // o dev com Azure CLI não precisa de auth…

        Assert.Throws<ConnectorSettingsException>(() => s.Auth!.RequireComplete());   // …o client credentials precisa
    }

    [Fact]
    public void Model_map_can_be_overridden_per_tenant()
    {
        D365InboundSettings s = D365InboundSettings.Parse(
            """{"url":"https://erp.example","modelTypes":{"01":"GoodsInvoice55","se":"ServiceNfse"}}""");

        Assert.Equal(DocumentType.GoodsInvoice55, s.ModelTypes["01"]);
        Assert.Equal(DocumentType.ServiceNfse, s.ModelTypes["SE"]);   // chave sem diferenciar maiúscula
        Assert.False(s.ModelTypes.ContainsKey("55"));                  // sobrescreve, não mescla
    }

    [Fact]
    public void Unknown_document_type_in_the_model_map_is_a_configuration_error()
    {
        Assert.Throws<ConnectorSettingsException>(() =>
            D365InboundSettings.Parse("""{"url":"https://erp.example","modelTypes":{"65":"Nfce"}}"""));
    }

    [Fact]
    public void Malformed_json_is_a_configuration_error()
    {
        Assert.Throws<ConnectorSettingsException>(() => D365InboundSettings.Parse("{url:"));
    }
}
