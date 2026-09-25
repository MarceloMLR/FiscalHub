using System.Text.Json;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Roda o feed contra um F&amp;O REAL. Opt-in: só roda com <c>FISCALHUB_D365_URL</c> definida (e o
/// desenvolvedor logado no Azure CLI); sem ela aparece como pulado e não toca a rede — CI inclusive.
/// <list type="bullet">
/// <item><c>FISCALHUB_D365_URL</c> — ex.: https://fiscosysdev.operations.dynamics.com</item>
/// <item><c>FISCALHUB_D365_COMPANY</c> — opcional, ex.: brmf</item>
/// <item><c>FISCALHUB_D365_EXPECTED_ROWS</c> — opcional, ex.: 83 no fiscosysdev</item>
/// </list>
/// </summary>
public class D365ChangeFeedIntegrationTests
{
    private const int PageSize = 20;

    [D365IntegrationFact]
    public async Task Keyset_read_from_2015_neither_repeats_nor_skips()
    {
        string url = Environment.GetEnvironmentVariable("FISCALHUB_D365_URL")!;
        string? company = Environment.GetEnvironmentVariable("FISCALHUB_D365_COMPANY");
        string companies = string.IsNullOrWhiteSpace(company) ? "[]" : $"[\"{company}\"]";

        var capture = new RawBodyCapture(new HttpClientHandler());
        var logger = new D365ChangeFeedTests.ListLogger<D365ChangeFeed>();
        var profiles = new D365ChangeFeedTests.FakeProfiles
        {
            Profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = "Dynamics365",
                InboundSettings = $$"""{"url":"{{url}}","companies":{{companies}},"pageSize":{{PageSize}}}""",
                OutboundAdapter = "Avalara",
            },
        };
        var feed = new D365ChangeFeed(
            new HttpClient(capture), profiles, new AzureCliD365TokenProvider(TimeProvider.System),
            new D365ChangeFeedOptions(), TimeProvider.System, logger);

        var pages = new List<ChangeFeedPage>();
        await foreach (ChangeFeedPage page in feed.PullAsync("tenant-a", new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero)))
        {
            pages.Add(page);
        }

        int rows = capture.RecIds.Count;
        Assert.Equal(rows, capture.RecIds.Distinct().Count());                     // nenhum RecId repetido entre páginas
        Assert.Equal(rows / PageSize + 1, pages.Count);                            // páginas cheias + a curta final
        Assert.Equal(rows, pages.Sum(p => p.References.Count) + logger.Warnings.Count);   // cada cabeçalho: referência ou aviso
        Assert.All(pages.SelectMany(p => p.References), r => Assert.Contains('|', r.NaturalKey));
        Assert.NotNull(pages[^1].HighWatermark);                                   // última página: relógio do F&O

        if (int.TryParse(Environment.GetEnvironmentVariable("FISCALHUB_D365_EXPECTED_ROWS"), out int expected))
        {
            Assert.Equal(expected, rows);   // fiscosysdev/brmf: 83 cabeçalhos em 5 páginas de 20
        }
    }

    /// <summary>Guarda os FiscalDocumentRecId de cada resposta crua, sem alterar o que o feed lê.</summary>
    private sealed class RawBodyCapture(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<long> RecIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                RecIds.AddRange(doc.RootElement.GetProperty("value").EnumerateArray()
                    .Select(row => row.GetProperty("FiscalDocumentRecId").GetInt64()));
            }

            return response;
        }
    }
}

/// <summary>Fato que só roda com <c>FISCALHUB_D365_URL</c> definida; senão aparece como pulado.</summary>
public sealed class D365IntegrationFactAttribute : FactAttribute
{
    public D365IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FISCALHUB_D365_URL")))
        {
            Skip = "Integração com F&O real: defina FISCALHUB_D365_URL (e faça az login) para rodar.";
        }
    }
}
