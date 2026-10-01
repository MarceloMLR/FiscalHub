using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;
using FiscalHub.Application.Inbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o rebobinamento pela tela (spec automatic-integration-panel, design D7): o mesmo lease do coletor, a
/// gravação condicionada a ele, só para trás, e só num cursor que já tem marca. O lease é liberado sempre. O tenant é o do
/// login.
/// </summary>
public class ChangeFeedRewindTests
{
    private const string Resource = "changefeed:Dynamics365:tenant-a";
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mark = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset September = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public async Task Rewinds_to_an_earlier_instant_and_says_the_mark_before_and_after()
    {
        var h = new Harness().WithMark("tenant-a", Mark);

        RewindResult result = await h.Rewind.RewindAsync(September);

        Assert.Equal(RewindStatus.Rewound, result.Status);
        Assert.Equal(Mark, result.Previous);
        Assert.Equal(September, result.Watermark);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero), h.Cursors.Items["tenant-a"].Watermark);
        Assert.Equal([Resource], h.Leases.Acquired);   // o mesmo recurso do coletor
        Assert.StartsWith("rewind:", h.Leases.Owners.Single());   // com um dono próprio
        Assert.Equal([Resource], h.Leases.Released);
    }

    [Fact]
    public async Task Collector_holding_the_lease_makes_it_busy_and_nothing_is_written()
    {
        var h = new Harness().WithMark("tenant-a", Mark);
        h.Leases.HeldByOther.Add(Resource);

        RewindResult result = await h.Rewind.RewindAsync(September);

        Assert.Equal(RewindStatus.Busy, result.Status);
        Assert.Contains("Tente novamente", result.Message);
        Assert.Equal(Mark, h.Cursors.Items["tenant-a"].Watermark);
        Assert.Equal(0, h.Cursors.RewindCalls);
    }

    [Fact]
    public async Task Without_cursor_there_is_no_mark_and_no_cursor_is_created()
    {
        var h = new Harness();

        RewindResult result = await h.Rewind.RewindAsync(September);

        Assert.Equal(RewindStatus.NoWatermark, result.Status);
        Assert.Contains("nenhuma busca", result.Message);
        Assert.Empty(h.Cursors.Items);
        Assert.Equal([Resource], h.Leases.Released);
    }

    [Fact]
    public async Task Cursor_without_mark_is_not_rewound()
    {
        var h = new Harness().WithMark("tenant-a", null);

        RewindResult result = await h.Rewind.RewindAsync(September);

        Assert.Equal(RewindStatus.NoWatermark, result.Status);
        Assert.Null(h.Cursors.Items["tenant-a"].Watermark);
    }

    [Theory]
    [InlineData(0)]    // igual à marca
    [InlineData(60)]   // depois da marca
    public async Task Target_that_is_not_earlier_than_the_mark_is_invalid(int minutesAfterMark)
    {
        var h = new Harness().WithMark("tenant-a", Mark);

        RewindResult result = await h.Rewind.RewindAsync(Mark.AddMinutes(minutesAfterMark));

        Assert.Equal(RewindStatus.Invalid, result.Status);
        Assert.Contains("anterior à última sincronização", result.Message);
        Assert.Equal(Mark, h.Cursors.Items["tenant-a"].Watermark);
        Assert.Equal([Resource], h.Leases.Released);
    }

    [Fact]
    public async Task Target_in_the_future_is_invalid_without_taking_the_lease()
    {
        var h = new Harness().WithMark("tenant-a", Now.AddDays(1));   // nem uma marca no futuro autoriza

        RewindResult result = await h.Rewind.RewindAsync(Now.AddMinutes(1));

        Assert.Equal(RewindStatus.Invalid, result.Status);
        Assert.Contains("no passado", result.Message);
        Assert.Empty(h.Leases.Acquired);
    }

    [Fact]
    public async Task Adapter_that_does_not_scan_has_nothing_to_rewind()
    {
        var h = new Harness(inboundAdapter: "iScala");

        RewindResult result = await h.Rewind.RewindAsync(September);

        Assert.Equal(RewindStatus.NotScanning, result.Status);
        Assert.Empty(h.Leases.Acquired);
    }

    [Fact]
    public async Task Lease_is_released_even_when_the_store_fails()
    {
        var h = new Harness().WithMark("tenant-a", Mark);
        h.Cursors.FailRewind = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Rewind.RewindAsync(September));

        Assert.Equal([Resource], h.Leases.Released);
    }

    [Fact]
    public async Task Lease_lost_before_the_write_makes_it_busy()
    {
        var h = new Harness().WithMark("tenant-a", Mark);
        h.Cursors.RefuseRewind = true;   // o UPDATE condicionado não achou o lease (venceu entre a leitura e a gravação)

        RewindResult result = await h.Rewind.RewindAsync(September);

        Assert.Equal(RewindStatus.Busy, result.Status);
        Assert.Equal(Mark, h.Cursors.Items["tenant-a"].Watermark);
    }

    [Fact]
    public async Task Only_the_logged_tenant_is_rewound()
    {
        var h = new Harness().WithMark("tenant-a", Mark).WithMark("tenant-c", Mark);

        await h.Rewind.RewindAsync(September);

        Assert.Equal(Mark, h.Cursors.Items["tenant-c"].Watermark);
    }

    private sealed class Harness
    {
        private readonly Profiles _profiles = new();

        public Harness(string inboundAdapter = "Dynamics365")
        {
            foreach (string tenant in new[] { "tenant-a", "tenant-c" })
            {
                _profiles.Items.Add(new TenantConnectorProfile
                {
                    TenantId = tenant,
                    Environment = "Sandbox",
                    InboundAdapter = inboundAdapter,
                    InboundSettings = """{"poll":{"enabled":true}}""",
                    OutboundAdapter = "Avalara",
                });
            }

            Rewind = new ChangeFeedRewind(_profiles, Cursors, Leases, [new Feed()], new Tenant("tenant-a"), new Clock());
        }

        public Cursors Cursors { get; } = new();

        public Leases Leases { get; } = new();

        public ChangeFeedRewind Rewind { get; }

        public Harness WithMark(string tenant, DateTimeOffset? watermark)
        {
            Cursors.Items[tenant] = new ChangeFeedCursor { TenantId = tenant, Origin = "Dynamics365", Watermark = watermark };
            return this;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
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

    private sealed class Profiles : IConnectorProfileStore
    {
        public List<TenantConnectorProfile> Items { get; } = [];

        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(p => p.TenantId == tenantId));

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Leases : ILeaseStore
    {
        public HashSet<string> HeldByOther { get; } = [];

        public List<string> Acquired { get; } = [];

        public List<string> Owners { get; } = [];

        public List<string> Released { get; } = [];

        public Task<bool> TryAcquireAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default)
        {
            if (HeldByOther.Contains(resource))
            {
                return Task.FromResult(false);
            }

            Acquired.Add(resource);
            Owners.Add(owner);
            return Task.FromResult(true);
        }

        public Task<bool> RenewAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(true);

        public Task ReleaseAsync(string resource, string owner, CancellationToken ct = default)
        {
            Released.Add(resource);
            return Task.CompletedTask;
        }
    }

    private sealed class Cursors : IChangeFeedCursorStore
    {
        public Dictionary<string, ChangeFeedCursor> Items { get; } = [];

        public int RewindCalls { get; private set; }

        public bool FailRewind { get; set; }

        public bool RefuseRewind { get; set; }

        public Task<ChangeFeedCursor?> GetAsync(string tenantId, string origin, CancellationToken ct = default)
            => Task.FromResult(Items.GetValueOrDefault(tenantId));

        public Task<bool> TryRewindWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
        {
            RewindCalls++;
            if (FailRewind)
            {
                throw new InvalidOperationException("banco fora");
            }

            if (RefuseRewind || !Items.TryGetValue(tenantId, out ChangeFeedCursor? current) || !(current.Watermark > watermark))
            {
                return Task.FromResult(false);
            }

            Items[tenantId] = current with { Watermark = watermark };
            return Task.FromResult(true);
        }

        public Task<ChangeFeedCursor> StartAsync(string tenantId, string origin, DateTimeOffset initialWatermark, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> TryAdvanceWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordSuccessAsync(string tenantId, string origin, DateTimeOffset polledAt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordFailureAsync(string tenantId, string origin, DateTimeOffset polledAt, string error, DateTimeOffset? notBefore, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
