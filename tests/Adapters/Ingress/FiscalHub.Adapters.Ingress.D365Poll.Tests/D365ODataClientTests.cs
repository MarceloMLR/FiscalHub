using System.Net;
using System.Text.Json;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Cliente OData compartilhado pelo feed e pela montagem: nas coleções de um documento lançado, segue o
/// <c>@odata.nextLink</c> até o fim, e o throttling vale em qualquer página.
/// </summary>
public class D365ODataClientTests
{
    private const string Env = "https://fiscosysdev.operations.dynamics.com";
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_all_joins_the_pages_following_the_next_link()
    {
        var http = new SequencedHttpMessageHandler()
            .Respond($$"""{"value":[{"FiscalDocumentLineRecId":1},{"FiscalDocumentLineRecId":2}],"@odata.nextLink":"{{Env}}/data/FSFiscalDocumentLineBRs?$skip=2"}""")
            .Respond("""{"value":[{"FiscalDocumentLineRecId":3}]}""");
        D365ODataClient client = Client(http, out _);

        IReadOnlyList<JsonElement> rows = await client.GetAllAsync(new Uri($"{Env}/data/FSFiscalDocumentLineBRs?cross-company=true"), Connection(), default);

        Assert.Equal([1L, 2L, 3L], rows.Select(r => r.GetProperty("FiscalDocumentLineRecId").GetInt64()));
        Assert.Equal($"{Env}/data/FSFiscalDocumentLineBRs?$skip=2", http.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task Throttling_applies_to_the_next_page_too()
    {
        var http = new SequencedHttpMessageHandler()
            .Respond($$"""{"value":[{"FiscalDocumentLineRecId":1}],"@odata.nextLink":"{{Env}}/data/FSFiscalDocumentLineBRs?$skip=1"}""")
            .Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(3)))
            .Respond("""{"value":[{"FiscalDocumentLineRecId":2}]}""");
        D365ODataClient client = Client(http, out D365ChangeFeedTests.FakeTime time);

        IReadOnlyList<JsonElement> rows = await client.GetAllAsync(new Uri($"{Env}/data/FSFiscalDocumentLineBRs"), Connection(), default);

        Assert.Equal(2, rows.Count);
        Assert.Equal([TimeSpan.FromSeconds(3)], time.Delays);
        Assert.Equal(http.Requests[1].RequestUri, http.Requests[2].RequestUri);   // repetiu a mesma página
    }

    [Fact]
    public async Task Rows_survive_after_the_response_is_disposed()
    {
        var http = new SequencedHttpMessageHandler().Respond("""{"value":[{"TaxAmount":100.50}]}""");
        D365ODataClient client = Client(http, out _);

        IReadOnlyList<JsonElement> rows = await client.GetAllAsync(new Uri($"{Env}/data/X"), Connection(), default);

        Assert.Equal("100.50", rows.Single().GetProperty("TaxAmount").GetRawText());   // número como veio
    }

    private static D365ODataClient Client(SequencedHttpMessageHandler http, out D365ChangeFeedTests.FakeTime time)
    {
        time = new D365ChangeFeedTests.FakeTime(Now);
        return new D365ODataClient(new HttpClient(http), new D365ChangeFeedTests.FakeTokens(), new D365ChangeFeedOptions(), time);
    }

    private static D365Connection Connection() => new("tenant-a", new Uri(Env), Auth: null);
}
