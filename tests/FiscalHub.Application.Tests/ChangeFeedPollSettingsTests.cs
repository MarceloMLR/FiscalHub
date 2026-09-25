using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>Especifica a leitura da seção <c>poll</c> das settings: padrões, valores e o que é inválido.</summary>
public class ChangeFeedPollSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"url":"https://erp.example/"}""")]
    public void Missing_poll_section_means_disabled_with_defaults(string? json)
    {
        ChangeFeedPollSettings settings = ChangeFeedPollSettings.Parse(json);

        Assert.False(settings.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Interval);
        Assert.Equal(TimeSpan.FromSeconds(300), settings.Overlap);
        Assert.Null(settings.StartFrom);
    }

    [Fact]
    public void Reads_explicit_values_ignoring_the_adapter_keys()
    {
        ChangeFeedPollSettings settings = ChangeFeedPollSettings.Parse("""
            {"url":"https://erp.example/","pageSize":20,
             "poll":{"enabled":true,"intervalSeconds":300,"overlapSeconds":120,"startFrom":"2015-01-01T00:00:00Z"}}
            """);

        Assert.True(settings.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(300), settings.Interval);
        Assert.Equal(TimeSpan.FromSeconds(120), settings.Overlap);
        Assert.Equal(new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero), settings.StartFrom);
    }

    [Fact]
    public void StartFrom_with_offset_is_normalized_to_utc()
    {
        ChangeFeedPollSettings settings = ChangeFeedPollSettings.Parse("""{"poll":{"startFrom":"2015-01-01T00:00:00-03:00"}}""");

        Assert.Equal(TimeSpan.Zero, settings.StartFrom!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2015, 1, 1, 3, 0, 0, TimeSpan.Zero), settings.StartFrom);
    }

    [Fact]
    public void Enabled_poll_without_other_keys_uses_defaults()
    {
        ChangeFeedPollSettings settings = ChangeFeedPollSettings.Parse("""{"poll":{"enabled":true}}""");

        Assert.True(settings.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Interval);
        Assert.Equal(TimeSpan.FromSeconds(300), settings.Overlap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Overlap_below_one_second_is_a_configuration_error(int overlap)
    {
        // Sobreposição zero pularia as linhas com o mesmo timestamp da marca que ficaram para uma página não lida.
        Assert.Throws<ConnectorSettingsException>(() =>
            ChangeFeedPollSettings.Parse($$$"""{"poll":{"enabled":true,"overlapSeconds":{{{overlap}}}}}"""));
    }

    [Fact]
    public void Non_positive_interval_is_a_configuration_error()
    {
        Assert.Throws<ConnectorSettingsException>(() =>
            ChangeFeedPollSettings.Parse("""{"poll":{"enabled":true,"intervalSeconds":0}}"""));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"poll":{"enabled":"sim"}}""")]
    [InlineData("""{"poll":{"startFrom":"ontem"}}""")]
    public void Malformed_json_is_a_configuration_error(string json)
    {
        Assert.Throws<ConnectorSettingsException>(() => ChangeFeedPollSettings.Parse(json));
    }
}
