using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o estado que o /info e o selo mostram (spec automatic-integration): ligado só quando o adapter de entrada
/// varre e o poll.enabled está ligado. É derivado do perfil, e não um campo gravado (design D1, D4).
/// </summary>
public class AutomaticIntegrationTests
{
    private static readonly string[] Scanning = ["Dynamics365"];

    [Fact]
    public void On_when_the_adapter_scans_and_poll_is_enabled()
    {
        Assert.True(AutomaticIntegration.IsOn(Profile("Dynamics365", """{"url":"https://erp/","poll":{"enabled":true}}"""), Scanning));
    }

    [Theory]
    [InlineData("Dynamics365", """{"poll":{"enabled":false,"intervalSeconds":300}}""")]   // desligado de propósito
    [InlineData("Dynamics365", """{"url":"https://erp/"}""")]                            // sem a seção poll
    [InlineData("iScala", """{"poll":{"enabled":true}}""")]                              // adapter que não varre, poll por SQL
    [InlineData("Dynamics365", "{not json")]                                             // settings ilegíveis
    [InlineData("Dynamics365", """{"poll":{"enabled":true,"overlapSeconds":0}}""")]      // seção que o coletor não lê
    public void Off_otherwise(string inboundAdapter, string inboundSettings)
    {
        Assert.False(AutomaticIntegration.IsOn(Profile(inboundAdapter, inboundSettings), Scanning));
    }

    [Fact]
    public void Off_without_a_profile()
    {
        Assert.False(AutomaticIntegration.IsOn(null, Scanning));
    }

    [Fact]
    public void Off_when_no_feed_is_registered()
    {
        Assert.False(AutomaticIntegration.IsOn(Profile("Dynamics365", """{"poll":{"enabled":true}}"""), []));
    }

    // ---------- o adapter varre: o selo aparece, verde ou vermelho (establishment-and-readable-dashboard, D12) ----------

    [Theory]
    [InlineData("""{"poll":{"enabled":true}}""")]
    [InlineData("""{"poll":{"enabled":false}}""")]
    [InlineData("""{"url":"https://erp/"}""")]   // sem a seção poll: varre, e está desligada
    [InlineData("{not json")]                    // ilegível: varre, e conta como desligada
    public void Scans_when_the_inbound_adapter_has_a_registered_feed_whatever_the_poll(string inboundSettings)
    {
        Assert.True(AutomaticIntegration.Scans(Profile("Dynamics365", inboundSettings), Scanning));
    }

    [Fact]
    public void Does_not_scan_with_an_adapter_without_a_feed_even_with_poll_enabled()
    {
        Assert.False(AutomaticIntegration.Scans(Profile("iScala", """{"poll":{"enabled":true}}"""), Scanning));
    }

    [Fact]
    public void Does_not_scan_without_a_profile()
    {
        Assert.False(AutomaticIntegration.Scans(null, Scanning));
    }

    private static TenantConnectorProfile Profile(string inboundAdapter, string inboundSettings) => new()
    {
        TenantId = "tenant-a",
        Environment = "Sandbox",
        InboundAdapter = inboundAdapter,
        InboundSettings = inboundSettings,
        OutboundAdapter = "Avalara",
    };
}
