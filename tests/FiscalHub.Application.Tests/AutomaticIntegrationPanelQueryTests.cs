using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a leitura do painel da integração automática (spec automatic-integration-panel, design D6): o que o coletor
/// registrou no cursor do tenant logado, e o <c>startFrom</c> do perfil, só como leitura. Adapter que não varre não tem
/// painel.
/// </summary>
public class AutomaticIntegrationPanelQueryTests
{
    private static readonly DateTimeOffset Watermark = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Adapter_that_does_not_scan_has_no_panel()
    {
        var h = new Harness("tenant-b", Profile("tenant-b", "iScala", """{"poll":{"enabled":true}}"""));

        Assert.Null(await h.Query.GetAsync());
    }

    [Fact]
    public async Task Tenant_without_profile_has_no_panel()
    {
        var h = new Harness("tenant-a");

        Assert.Null(await h.Query.GetAsync());
    }

    [Fact]
    public async Task Without_cursor_the_panel_has_no_cursor_and_says_where_the_first_pass_starts()
    {
        var h = new Harness("tenant-a", Profile("tenant-a", "Dynamics365", """{"poll":{"enabled":true,"startFrom":"2015-01-01T00:00:00Z"}}"""));

        AutomaticIntegrationPanel? panel = await h.Query.GetAsync();

        Assert.NotNull(panel);
        Assert.Null(panel.Cursor);
        Assert.Equal(new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero), panel.StartFrom);
    }

    [Fact]
    public async Task Cursor_fields_come_as_the_collector_recorded_them()
    {
        var h = new Harness("tenant-a", Profile("tenant-a", "Dynamics365", """{"poll":{"enabled":true}}"""));
        h.Cursors.Items["tenant-a"] = new ChangeFeedCursor
        {
            TenantId = "tenant-a",
            Origin = "Dynamics365",
            Watermark = Watermark,
            LastPolledAt = Watermark.AddSeconds(20),
            NotBefore = Watermark.AddMinutes(1),
            ConsecutiveFailures = 3,
            LastError = "AADSTS7000215: Invalid client secret provided.",
        };

        AutomaticIntegrationPanel? panel = await h.Query.GetAsync();

        Assert.Equal(Watermark, panel!.Cursor!.Watermark);
        Assert.Equal(Watermark.AddSeconds(20), panel.Cursor.LastPolledAt);
        Assert.Equal(Watermark.AddMinutes(1), panel.Cursor.NotBefore);
        Assert.Equal(3, panel.Cursor.ConsecutiveFailures);
        Assert.Equal("AADSTS7000215: Invalid client secret provided.", panel.Cursor.LastError);
        Assert.Null(panel.StartFrom);   // sem startFrom gravado: a primeira passada, se houvesse, nasceria em "agora"
        Assert.Equal([("tenant-a", "Dynamics365")], h.Cursors.Reads);   // a origem é o adapter de entrada do perfil
    }

    [Fact]
    public async Task Panel_is_always_of_the_logged_tenant()
    {
        var h = new Harness("tenant-a",
            Profile("tenant-a", "Dynamics365", """{"poll":{"enabled":true}}"""),
            Profile("tenant-c", "Dynamics365", """{"poll":{"enabled":true,"startFrom":"2020-01-01T00:00:00Z"}}"""));
        h.Cursors.Items["tenant-c"] = new ChangeFeedCursor { TenantId = "tenant-c", Origin = "Dynamics365", Watermark = Watermark };

        AutomaticIntegrationPanel? panel = await h.Query.GetAsync();

        Assert.Null(panel!.Cursor);
        Assert.Null(panel.StartFrom);
    }

    private static TenantConnectorProfile Profile(string tenant, string inboundAdapter, string inboundSettings) => new()
    {
        TenantId = tenant,
        Environment = "Sandbox",
        InboundAdapter = inboundAdapter,
        InboundSettings = inboundSettings,
        OutboundAdapter = "Avalara",
    };

    private sealed class Harness
    {
        public Harness(string tenant, params TenantConnectorProfile[] profiles)
        {
            Query = new AutomaticIntegrationPanelQuery(new Profiles(profiles), Cursors, [new Feed()], new Tenant(tenant));
        }

        public Cursors Cursors { get; } = new();

        public AutomaticIntegrationPanelQuery Query { get; }
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class Feed : IDocumentChangeFeed
    {
        public string Origin => "Dynamics365";

        public IAsyncEnumerable<ChangeFeedPage> PullAsync(string tenantId, DateTimeOffset since, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Profiles(TenantConnectorProfile[] items) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(items.FirstOrDefault(p => p.TenantId == tenantId));

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Cursors : IChangeFeedCursorStore
    {
        public Dictionary<string, ChangeFeedCursor> Items { get; } = [];

        public List<(string Tenant, string Origin)> Reads { get; } = [];

        public Task<ChangeFeedCursor?> GetAsync(string tenantId, string origin, CancellationToken ct = default)
        {
            Reads.Add((tenantId, origin));
            return Task.FromResult(Items.GetValueOrDefault(tenantId));
        }

        public Task<ChangeFeedCursor> StartAsync(string tenantId, string origin, DateTimeOffset initialWatermark, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> TryAdvanceWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> TryRewindWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordSuccessAsync(string tenantId, string origin, DateTimeOffset polledAt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordFailureAsync(string tenantId, string origin, DateTimeOffset polledAt, string error, DateTimeOffset? notBefore, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
