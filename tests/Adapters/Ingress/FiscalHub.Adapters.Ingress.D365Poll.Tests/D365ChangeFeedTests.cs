using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using System.Web;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Especifica o feed do D365 (spec d365-change-feed): consulta, paginação keyset em
/// (SysModifiedDateTime, FiscalDocumentRecId), marca alta pelo header Date, mapeamento e token.
/// HTTP falso (fila de respostas), token falso, relógio falso.
/// </summary>
public class D365ChangeFeedTests
{
    private const string Env = "https://fiscosysdev.operations.dynamics.com";
    private static readonly DateTimeOffset Since2015 = new(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ServerNow = new(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);

    // ---------- primeira página ----------

    [Fact]
    public async Task First_page_without_companies()
    {
        var h = new Harness("""{"url":"https://fiscosysdev.operations.dynamics.com","pageSize":500}""");
        h.Http.Respond(Rows());

        await h.PullAllAsync(Since2015);

        HttpRequestMessage request = Assert.Single(h.Http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{Env}/data/FSFiscalDocumentBRs", request.RequestUri!.GetLeftPart(UriPartial.Path));
        NameValueCollection q = Query(request);
        Assert.Equal("true", q["cross-company"]);
        Assert.Equal("SysModifiedDateTime gt 2015-01-01T00:00:00Z", q["$filter"]);
        Assert.Equal("SysModifiedDateTime,FiscalDocumentRecId", q["$orderby"]);
        Assert.Equal("500", q["$top"]);
        Assert.Equal(
            ["dataAreaId", "Voucher", "Model", "Direction", "Status", "FiscalDocumentNumber", "FiscalDocumentSeries", "SysModifiedDateTime", "FiscalDocumentRecId"],
            q["$select"]!.Split(','));
        Assert.DoesNotContain("Status", q["$filter"]);
        Assert.DoesNotContain("Model", q["$filter"]);
        Assert.DoesNotContain("Direction", q["$filter"]);
    }

    [Fact]
    public async Task First_page_with_companies()
    {
        var h = new Harness("""{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf","brsp"]}""");
        h.Http.Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal(
            "SysModifiedDateTime gt 2015-01-01T00:00:00Z and (dataAreaId eq 'brmf' or dataAreaId eq 'brsp')",
            Query(h.Http.Requests.Single())["$filter"]);
    }

    [Fact]
    public async Task Since_with_a_fraction_is_floored_to_the_second()
    {
        // Arredondar para baixo só amplia a janela (lado seguro); a sobreposição já cobre o resto.
        var h = new Harness();
        h.Http.Respond(Rows());

        await h.PullAllAsync(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(900));

        Assert.Equal("SysModifiedDateTime gt 2026-09-25T12:00:00Z", Query(h.Http.Requests.Single())["$filter"]);
    }

    // ---------- keyset ----------

    [Fact]
    public async Task Three_pages_by_keyset_ending_on_the_short_page()
    {
        var h = new Harness(pageSize: 2);
        h.Http
            .Respond(Rows(Row("V1", "2017-01-01T10:00:00Z", 101), Row("V2", "2017-01-01T10:00:05Z", 102)))
            .Respond(Rows(Row("V3", "2017-01-02T10:00:00Z", 103), Row("V4", "2017-01-02T10:00:09Z", 104)))
            .Respond(Rows(Row("V5", "2017-01-03T10:00:00Z", 105)));

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal([2, 2, 1], pages.Select(p => p.References.Count));
        Assert.Equal(3, h.Http.Requests.Count);   // parou na página curta
        Assert.Equal(
            "(SysModifiedDateTime gt 2017-01-01T10:00:05Z) or (SysModifiedDateTime eq 2017-01-01T10:00:05Z and FiscalDocumentRecId gt 102)",
            Query(h.Http.Requests[1])["$filter"]);
        Assert.Equal(
            "(SysModifiedDateTime gt 2017-01-02T10:00:09Z) or (SysModifiedDateTime eq 2017-01-02T10:00:09Z and FiscalDocumentRecId gt 104)",
            Query(h.Http.Requests[2])["$filter"]);
        Assert.All(h.Http.Requests, r => Assert.Equal("2", Query(r)["$top"]));
    }

    [Fact]
    public async Task Anchor_reuses_the_raw_literal_of_the_response()
    {
        var h = new Harness(pageSize: 1);
        h.Http
            .Respond(Rows(Row("V1", "2017-01-21T21:23:19.123Z", 5637148912)))
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Contains("SysModifiedDateTime eq 2017-01-21T21:23:19.123Z and", Query(h.Http.Requests[1])["$filter"]);
    }

    [Fact]
    public async Task Anchor_is_combined_with_the_company_filter()
    {
        var h = new Harness("""{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"],"pageSize":1}""");
        h.Http
            .Respond(Rows(Row("BRMF21-10000027", "2017-01-21T21:23:19Z", 5637148912)))
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal(
            "((SysModifiedDateTime gt 2017-01-21T21:23:19Z) or (SysModifiedDateTime eq 2017-01-21T21:23:19Z and FiscalDocumentRecId gt 5637148912)) and (dataAreaId eq 'brmf')",
            Query(h.Http.Requests[1])["$filter"]);
    }

    [Fact]
    public async Task Timestamp_tie_on_the_page_boundary_neither_repeats_nor_skips()
    {
        var h = new Harness(pageSize: 2);
        const string t = "2017-01-21T21:23:19Z";
        h.Http
            .Respond(Rows(Row("V1", t, 100), Row("V2", t, 101)))
            .Respond(Rows(Row("V3", t, 102)));

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal($"(SysModifiedDateTime gt {t}) or (SysModifiedDateTime eq {t} and FiscalDocumentRecId gt 101)", Query(h.Http.Requests[1])["$filter"]);
        Assert.Equal(["brmf|V1", "brmf|V2", "brmf|V3"], pages.SelectMany(p => p.References).Select(r => r.NaturalKey));
    }

    [Fact]
    public async Task Full_page_followed_by_an_empty_page_ends_the_read()
    {
        var h = new Harness(pageSize: 2);
        h.Http
            .Respond(Rows(Row("V1", "2017-01-01T10:00:00Z", 1), Row("V2", "2017-01-01T10:00:01Z", 2)))
            .Respond(Rows());

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal([2, 0], pages.Select(p => p.References.Count));
        Assert.Equal(2, h.Http.Requests.Count);
    }

    [Fact]
    public async Task NextLink_in_the_response_is_ignored()
    {
        var h = new Harness(pageSize: 2);
        h.Http
            .Respond(Rows(nextLink: $"{Env}/data/FSFiscalDocumentBRs?$skip=2&$top=2",
                Row("V1", "2017-01-01T10:00:00Z", 1), Row("V2", "2017-01-01T10:00:01Z", 2)))
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        NameValueCollection second = Query(h.Http.Requests[1]);
        Assert.Null(second["$skip"]);   // offset perde linha sob escrita concorrente: não seguir
        Assert.Contains("FiscalDocumentRecId gt 2", second["$filter"]);
    }

    // ---------- marca alta ----------

    [Fact]
    public async Task Full_page_high_watermark_is_its_largest_timestamp()
    {
        var h = new Harness(pageSize: 2);
        h.Http
            .Respond(Rows(Row("V1", "2017-01-21T21:23:10Z", 1), Row("V2", "2017-01-21T21:23:19Z", 2)), date: ServerNow)
            .Respond(Rows(), date: ServerNow.AddSeconds(30));

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal(new DateTimeOffset(2017, 1, 21, 21, 23, 19, TimeSpan.Zero), pages[0].HighWatermark);
    }

    [Fact]
    public async Task Short_last_page_uses_the_date_of_the_first_response()
    {
        var h = new Harness(pageSize: 2);
        h.Http
            .Respond(Rows(Row("V1", "2017-01-21T21:23:10Z", 1), Row("V2", "2017-01-21T21:23:19Z", 2)), date: ServerNow)
            .Respond(Rows(Row("V3", "2017-01-21T21:24:00Z", 3)), date: ServerNow.AddSeconds(30));

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal(ServerNow, pages[1].HighWatermark);   // o relógio do início da varredura, não o da última resposta
    }

    [Fact]
    public async Task Empty_read_high_watermark_is_the_server_date()
    {
        var h = new Harness();
        h.Http.Respond(Rows(), date: ServerNow);

        ChangeFeedPage page = Assert.Single(await h.PullAllAsync(Since2015));

        Assert.Empty(page.References);
        Assert.Equal(ServerNow, page.HighWatermark);
    }

    [Fact]
    public async Task Empty_read_without_date_does_not_advance()
    {
        var h = new Harness();
        h.Http.Respond(Rows());

        ChangeFeedPage page = Assert.Single(await h.PullAllAsync(Since2015));

        Assert.Null(page.HighWatermark);
    }

    // ---------- mapeamento ----------

    [Fact]
    public async Task Goods_and_service_invoices_map_to_references()
    {
        var h = new Harness();
        h.Http.Respond(Rows(
            Row("BRMF21-10000027", "2017-01-21T21:23:19Z", 1, model: "55"),
            Row("BRMF21-10000019", "2016-11-28T20:58:29Z", 2, model: "SE")));

        DocumentReference[] refs = (await h.PullAllAsync(Since2015)).SelectMany(p => p.References).ToArray();

        Assert.Equal("tenant-a", refs[0].TenantId);
        Assert.Equal("brmf|BRMF21-10000027", refs[0].NaturalKey);
        Assert.Equal("d365/brmf/BRMF21-10000027", refs[0].Locator);
        Assert.Equal(DocumentType.GoodsInvoice55, refs[0].Type);
        Assert.Equal(IngestionTrigger.Event, refs[0].Trigger);
        Assert.Equal(DocumentType.ServiceNfse, refs[1].Type);
    }

    [Fact]
    public async Task Locator_segments_are_url_encoded()
    {
        var h = new Harness();
        h.Http.Respond(Rows(Row("NF/2017 01", "2017-01-21T21:23:19Z", 1)));

        DocumentReference reference = (await h.PullAllAsync(Since2015)).Single().References.Single();

        Assert.Equal("d365/brmf/NF%2F2017%2001", reference.Locator);
        Assert.Equal("brmf|NF/2017 01", reference.NaturalKey);
    }

    [Fact]
    public async Task Unmapped_model_and_empty_voucher_warn_and_do_not_stop_the_read()
    {
        var h = new Harness(pageSize: 3);
        h.Http.Respond(Rows(
            Row("V1", "2017-01-01T10:00:00Z", 1, model: "55"),
            Row("V2", "2017-01-01T10:00:01Z", 2, model: "65"),
            Row("", "2017-01-01T10:00:02Z", 3, model: "55")));
        h.Http.Respond(Rows());

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal(["brmf|V1"], pages[0].References.Select(r => r.NaturalKey));
        Assert.Equal(new DateTimeOffset(2017, 1, 1, 10, 0, 2, TimeSpan.Zero), pages[0].HighWatermark);   // os pulados contam como lidos
        Assert.Equal(2, h.Logger.Warnings.Count);
        Assert.Contains(h.Logger.Warnings, w => w.Contains("65") && w.Contains("V2") && w.Contains("brmf"));
        Assert.Equal(2, h.Http.Requests.Count);   // seguiu para a próxima página
    }

    // ---------- autenticação e configuração ----------

    [Fact]
    public async Task Requests_carry_the_bearer_token_for_the_tenant_environment()
    {
        var h = new Harness();
        h.Http.Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal("Bearer", h.Http.Requests.Single().Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", h.Http.Requests.Single().Headers.Authorization!.Parameter);
        D365Connection connection = h.Tokens.Connections.Single();
        Assert.Equal("tenant-a", connection.TenantId);
        Assert.Equal(new Uri(Env), connection.EnvironmentUrl);
    }

    [Fact]
    public async Task Invalid_settings_fail_without_calling_the_network()
    {
        var h = new Harness("""{"url":"","poll":{"enabled":true}}""");

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => h.PullAllAsync(Since2015));

        Assert.Empty(h.Http.Requests);
        Assert.Empty(h.Tokens.Connections);
    }

    [Fact]
    public async Task Missing_profile_is_a_configuration_error()
    {
        var h = new Harness();
        h.Profiles.Profile = null;

        await Assert.ThrowsAsync<ConnectorSettingsException>(() => h.PullAllAsync(Since2015));
    }

    // ---------- throttling ----------

    [Fact]
    public async Task Retry_after_in_seconds_waits_and_repeats_the_same_url()
    {
        var h = new Harness();
        h.Http
            .Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(5)))
            .Respond(Rows(Row("V1", "2017-01-01T10:00:00Z", 1)));

        List<ChangeFeedPage> pages = await h.PullAllAsync(Since2015);

        Assert.Equal([TimeSpan.FromSeconds(5)], h.Time.Delays);
        Assert.Equal(h.Http.Requests[0].RequestUri, h.Http.Requests[1].RequestUri);
        Assert.Single(pages.Single().References);
    }

    [Fact]
    public async Task Retry_after_as_http_date_waits_until_that_instant()
    {
        var h = new Harness();
        h.Http
            .Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(h.Time.GetUtcNow().AddSeconds(7)))
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal([TimeSpan.FromSeconds(7)], h.Time.Delays);
    }

    [Fact]
    public async Task Throttling_on_a_later_page_repeats_the_same_keyset_anchor()
    {
        var h = new Harness(pageSize: 1);
        h.Http
            .Respond(Rows(Row("V1", "2017-01-01T10:00:00Z", 1)))
            .Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(2)))
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal(h.Http.Requests[1].RequestUri, h.Http.Requests[2].RequestUri);
        Assert.Contains("FiscalDocumentRecId gt 1", Query(h.Http.Requests[2])["$filter"]);
    }

    [Fact]
    public async Task Service_unavailable_with_retry_after_is_retried()
    {
        var h = new Harness();
        h.Http
            .Respond("{}", HttpStatusCode.ServiceUnavailable, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(1)))
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal(2, h.Http.Requests.Count);
    }

    [Fact]
    public async Task Without_retry_after_the_wait_grows_exponentially()
    {
        var h = new Harness();
        h.Http
            .Respond("{}", (HttpStatusCode)429)
            .Respond("{}", (HttpStatusCode)429)
            .Respond("{}", (HttpStatusCode)429)
            .Respond(Rows());

        await h.PullAllAsync(Since2015);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], h.Time.Delays);
    }

    [Fact]
    public async Task Retry_after_above_the_cap_fails_as_throttled_without_waiting()
    {
        var h = new Harness();
        h.Http.Respond("{}", (HttpStatusCode)429, r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(600)));

        var ex = await Assert.ThrowsAsync<ChangeFeedThrottledException>(() => h.PullAllAsync(Since2015));

        Assert.Equal(TimeSpan.FromSeconds(600), ex.RetryAfter);
        Assert.Empty(h.Time.Delays);
        Assert.Single(h.Http.Requests);
    }

    [Fact]
    public async Task Exhausted_retries_fail_as_throttled()
    {
        var h = new Harness();
        for (int i = 0; i < 5; i++)
        {
            h.Http.Respond("{}", (HttpStatusCode)429);
        }

        await Assert.ThrowsAsync<ChangeFeedThrottledException>(() => h.PullAllAsync(Since2015));

        Assert.Equal(5, h.Http.Requests.Count);
        Assert.Equal(4, h.Time.Delays.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Non_transient_errors_fail_without_retrying(HttpStatusCode status)
    {
        var h = new Harness();
        h.Http.Respond("""{"error":{"message":"nope"}}""", status);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => h.PullAllAsync(Since2015));

        Assert.Equal(status, ex.StatusCode);
        Assert.Single(h.Http.Requests);
        Assert.Empty(h.Time.Delays);
    }

    // ---------- apoio ----------

    private static NameValueCollection Query(HttpRequestMessage request) => HttpUtility.ParseQueryString(request.RequestUri!.Query);

    private static object Row(string voucher, string modified, long recId, string model = "55", string company = "brmf") => new Dictionary<string, object?>
    {
        ["dataAreaId"] = company,
        ["Voucher"] = voucher,
        ["Model"] = model,
        ["Direction"] = "Outgoing",
        ["Status"] = "Approved",
        ["FiscalDocumentNumber"] = "000002",
        ["FiscalDocumentSeries"] = "02",
        ["SysModifiedDateTime"] = modified,
        ["FiscalDocumentRecId"] = recId,
    };

    private static string Rows(params object[] rows) => Rows(nextLink: null, rows);

    private static string Rows(string? nextLink, params object[] rows)
    {
        var body = new Dictionary<string, object?> { ["@odata.context"] = $"{Env}/data/$metadata#FSFiscalDocumentBRs", ["value"] = rows };
        if (nextLink is not null)
        {
            body["@odata.nextLink"] = nextLink;
        }

        return JsonSerializer.Serialize(body);
    }

    private sealed class Harness
    {
        public Harness(string? settings = null, int pageSize = 500)
        {
            Profiles.Profile = new TenantConnectorProfile
            {
                TenantId = "tenant-a",
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = "Dynamics365",
                InboundSettings = settings ?? $$"""{"url":"{{Env}}","pageSize":{{pageSize}}}""",
                OutboundAdapter = "Avalara",
            };
            Feed = new D365ChangeFeed(new HttpClient(Http), Profiles, Tokens, new D365ChangeFeedOptions(), Time, Logger);
        }

        public SequencedHttpMessageHandler Http { get; } = new();
        public FakeProfiles Profiles { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeTime Time { get; } = new(ServerNow);
        public ListLogger<D365ChangeFeed> Logger { get; } = new();
        public D365ChangeFeed Feed { get; }

        public async Task<List<ChangeFeedPage>> PullAllAsync(DateTimeOffset since)
        {
            var pages = new List<ChangeFeedPage>();
            await foreach (ChangeFeedPage page in Feed.PullAsync("tenant-a", since))
            {
                pages.Add(page);
            }

            return pages;
        }
    }

    internal sealed class FakeProfiles : IConnectorProfileStore
    {
        public TenantConnectorProfile? Profile { get; set; }

        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(Profile);

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>(Profile is null ? [] : [Profile]);
    }

    internal sealed class FakeTokens : ID365TokenProvider
    {
        public List<D365Connection> Connections { get; } = [];

        public Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default)
        {
            Connections.Add(connection);
            return Task.FromResult("tok-123");
        }
    }

    /// <summary>Relógio falso: registra cada espera e a libera na hora, sem dormir.</summary>
    internal sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                Delays.Add(dueTime);
                _ = Task.Run(() => callback(state));
            }

            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
