using System.Runtime.CompilerServices;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o worker do feed de mudanças (spec change-feed-polling): seleção de tenants, intervalo,
/// marca d'água, sobreposição, avanço por página, "falha não avança", lease com fencing e isolamento.
/// </summary>
public class ChangeFeedPollerTests
{
    private const string Origin = "Dynamics365";
    private const string Owner = "replica-1";
    private static readonly DateTimeOffset Now = At(12, 10);

    // ---------- seleção de tenants ----------

    [Fact]
    public async Task Tenant_with_the_adapter_and_poll_enabled_is_polled()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Feed.Read("tenant-a", Page(At(12, 5), "A1"));

        await h.RunAsync();

        Assert.Single(h.Feed.Calls, c => c.Tenant == "tenant-a");
        Assert.Equal(["tenant-a|A1"], h.Queue.Keys);
    }

    [Theory]
    [InlineData("""{"poll":{"enabled":false}}""")]
    [InlineData("{}")]
    public async Task Disabled_or_missing_poll_does_not_poll_nor_touch_the_cursor(string settings)
    {
        var h = new Harness().WithTenant("tenant-a", settings);

        await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
        Assert.Null(h.Cursors.Find("tenant-a"));
    }

    [Fact]
    public async Task Tenant_of_another_adapter_is_ignored()
    {
        var h = new Harness().WithTenant("tenant-b", Enabled, inboundAdapter: "iScala");

        await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
    }

    // ---------- intervalo ----------

    [Fact]
    public async Task Within_the_interval_is_skipped()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0), lastPolledAt: Now.AddSeconds(-30));

        await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
    }

    [Fact]
    public async Task Elapsed_interval_polls()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0), lastPolledAt: Now.AddSeconds(-61));

        await h.RunAsync();

        Assert.Single(h.Feed.Calls);
    }

    [Fact]
    public async Task Interval_is_per_tenant()
    {
        var h = new Harness()
            .WithTenant("tenant-a", """{"poll":{"enabled":true,"intervalSeconds":300}}""")
            .WithTenant("tenant-c", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0), lastPolledAt: Now.AddSeconds(-120));
        h.Cursors.Seed("tenant-c", At(12, 0), lastPolledAt: Now.AddSeconds(-120));

        await h.RunAsync();

        Assert.Equal(["tenant-c"], h.Feed.Calls.Select(c => c.Tenant));   // 120s < 300s; 120s > 60s
    }

    [Fact]
    public async Task Throttling_not_before_defers_even_after_the_interval()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0), lastPolledAt: Now.AddMinutes(-5), notBefore: Now.AddMinutes(3));

        await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
    }

    [Fact]
    public async Task Throttled_read_sets_not_before_and_counts_a_failure()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Fail(new ChangeFeedThrottledException(TimeSpan.FromMinutes(10), "429")));

        await h.RunAsync();

        ChangeFeedCursor cursor = h.Cursors.Find("tenant-a")!;
        Assert.Equal(Now.AddMinutes(10), cursor.NotBefore);
        Assert.Equal(1, cursor.ConsecutiveFailures);
        Assert.Equal(At(12, 0), cursor.Watermark);
    }

    // ---------- marca d'água ----------

    [Fact]
    public async Task First_poll_creates_the_watermark_from_startFrom()
    {
        var h = new Harness().WithTenant("tenant-a", """{"poll":{"enabled":true,"startFrom":"2015-01-01T00:00:00Z"}}""");

        await h.RunAsync();

        var start = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(start, h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Equal(start.AddSeconds(-300), h.Feed.Calls.Single().Since);
    }

    [Fact]
    public async Task First_poll_without_startFrom_starts_now()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);

        await h.RunAsync();

        Assert.Equal(Now, h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Equal(Now.AddSeconds(-300), h.Feed.Calls.Single().Since);
    }

    [Fact]
    public async Task Since_is_the_watermark_minus_the_overlap()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));

        await h.RunAsync();

        Assert.Equal(At(11, 55), h.Feed.Calls.Single().Since);
    }

    [Fact]
    public async Task Overlap_repeat_is_reenqueued_and_the_watermark_does_not_regress()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(11, 50));
        h.Feed.Read("tenant-a", Page(At(12, 0), "A"));
        h.Feed.Read("tenant-a", Page(At(11, 58), "A"));   // A de novo, dentro da janela

        await h.RunAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        await h.RunAsync();

        Assert.Equal(["tenant-a|A", "tenant-a|A"], h.Queue.Keys);   // o motor não filtra: dedupe é da esteira
        Assert.Equal(At(12, 0), h.Cursors.Find("tenant-a")!.Watermark);
    }

    [Fact]
    public async Task Overlap_of_zero_is_a_configuration_error_without_querying()
    {
        var h = new Harness().WithTenant("tenant-a", """{"poll":{"enabled":true,"overlapSeconds":0}}""");

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
        Assert.True(summary.Failures.ContainsKey("tenant-a"));
        Assert.Equal(1, h.Cursors.Find("tenant-a")!.ConsecutiveFailures);
    }

    // ---------- avanço por página e falha que não avança ----------

    [Fact]
    public async Task Several_pages_advance_page_by_page()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A", "B"), Page(At(12, 2), "C"), Page(At(12, 3), "D"));

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Equal(["tenant-a|A", "tenant-a|B", "tenant-a|C", "tenant-a|D"], h.Queue.Keys);
        Assert.Equal([At(12, 1), At(12, 2), At(12, 3)], h.Cursors.Advances);
        Assert.Equal(At(12, 3), h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Equal(4, summary.ReferencesEnqueued);
    }

    [Fact]
    public async Task Failure_in_the_query_does_not_advance()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Fail(new HttpRequestException("F&O fora")));

        await h.RunAsync();

        ChangeFeedCursor cursor = h.Cursors.Find("tenant-a")!;
        Assert.Equal(At(12, 0), cursor.Watermark);
        Assert.Equal(1, cursor.ConsecutiveFailures);
        Assert.Equal("F&O fora", cursor.LastError);
        Assert.Equal(Now, cursor.LastPolledAt);
    }

    [Fact]
    public async Task Next_pass_after_a_failure_retries_from_the_preserved_watermark()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Fail(new HttpRequestException("F&O fora")));

        await h.RunAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        await h.RunAsync();

        Assert.Equal([At(11, 55), At(11, 55)], h.Feed.Calls.Select(c => c.Since));
    }

    [Fact]
    public async Task Failure_on_the_second_page_keeps_the_first()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"), Fail(new HttpRequestException("caiu")));

        await h.RunAsync();

        Assert.Equal(["tenant-a|A"], h.Queue.Keys);
        Assert.Equal(At(12, 1), h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Equal(1, h.Cursors.Find("tenant-a")!.ConsecutiveFailures);
    }

    [Fact]
    public async Task Enqueue_failure_does_not_advance_to_that_page()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A", "B"));
        h.Queue.FailOn = "tenant-a|B";

        await h.RunAsync();

        Assert.Equal(At(12, 0), h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Empty(h.Cursors.Advances);
        Assert.Equal(1, h.Cursors.Find("tenant-a")!.ConsecutiveFailures);
    }

    [Fact]
    public async Task Empty_page_advances_to_its_high_watermark()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 5)));

        await h.RunAsync();

        Assert.Empty(h.Queue.Keys);
        Assert.Equal(At(12, 5), h.Cursors.Find("tenant-a")!.Watermark);
    }

    [Fact]
    public async Task Page_without_high_watermark_does_not_advance()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(null));

        await h.RunAsync();

        Assert.Empty(h.Cursors.Advances);
        Assert.Equal(0, h.Cursors.Find("tenant-a")!.ConsecutiveFailures);   // não é falha
    }

    [Fact]
    public async Task Advance_is_not_attempted_when_the_page_is_not_newer()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(11, 59), "A"), Page(At(12, 0), "B"));

        await h.RunAsync();

        Assert.Empty(h.Cursors.Advances);
        Assert.Equal(["tenant-a|A", "tenant-a|B"], h.Queue.Keys);
    }

    // ---------- teto de páginas ----------

    [Fact]
    public async Task Page_cap_stops_the_tenant_and_moves_to_the_next()
    {
        var h = new Harness(maxPagesPerPass: 2).WithTenant("tenant-a", Enabled).WithTenant("tenant-c", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"), Page(At(12, 2), "B"), Page(At(12, 3), "C"));
        h.Feed.Read("tenant-c", Page(At(12, 4), "X"));

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Equal(["tenant-a|A", "tenant-a|B", "tenant-c|X"], h.Queue.Keys);
        Assert.Equal(At(12, 2), h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Empty(summary.Stalled);   // a marca saiu da janela: está progredindo
    }

    [Fact]
    public async Task Page_cap_without_leaving_the_overlap_window_is_reported_as_stalled()
    {
        var h = new Harness(maxPagesPerPass: 2).WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(11, 56), "A"), Page(At(11, 57), "B"), Page(At(11, 58), "C"));

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Equal(["tenant-a"], summary.Stalled);
    }

    // ---------- lease ----------

    [Fact]
    public async Task Lease_held_by_another_replica_skips_without_querying()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Leases.HeldBy(Resource("tenant-a"), "replica-2", Now.AddMinutes(1));

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
        Assert.Equal(At(12, 0), h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Null(h.Cursors.Find("tenant-a")!.LastPolledAt);
        Assert.Equal(1, summary.LeasesBusy);
    }

    [Fact]
    public async Task Expired_lease_of_another_replica_is_taken()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Leases.HeldBy(Resource("tenant-a"), "replica-2", Now.AddSeconds(-1));

        await h.RunAsync();

        Assert.Single(h.Feed.Calls);
    }

    [Fact]
    public async Task Lease_lost_on_renewal_stops_without_writing_the_page()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"), Page(At(12, 2), "B"));
        h.Leases.RenewFails = true;

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Equal(["tenant-a|A"], h.Queue.Keys);   // enfileirou antes de descobrir: repetição, não perda
        Assert.Empty(h.Cursors.Advances);
        Assert.Equal(0, h.Cursors.Find("tenant-a")!.ConsecutiveFailures);   // não é falha do tenant
        Assert.Equal(["tenant-a"], summary.LeasesLost);
    }

    [Fact]
    public async Task Advance_refused_by_the_store_stops_without_counting_a_failure()
    {
        // Renovou, pausou, o lease expirou e outra réplica tomou: a gravação condicionada é recusada.
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"), Page(At(12, 2), "B"));
        h.Cursors.RefuseAdvance = true;

        ChangeFeedPassSummary summary = await h.RunAsync();

        ChangeFeedCursor cursor = h.Cursors.Find("tenant-a")!;
        Assert.Equal(At(12, 0), cursor.Watermark);
        Assert.Equal(0, cursor.ConsecutiveFailures);
        Assert.Null(cursor.LastPolledAt);   // quem registra o poll é a réplica dona do lease
        Assert.Equal(["tenant-a|A"], h.Queue.Keys);   // parou na primeira página
        Assert.Equal(["tenant-a"], summary.LeasesLost);
    }

    [Fact]
    public async Task Lease_is_released_after_a_failure()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Fail(new HttpRequestException("caiu")));

        await h.RunAsync();

        Assert.Equal([Resource("tenant-a")], h.Leases.Released);
        Assert.False(h.Leases.IsHeld(Resource("tenant-a")));
    }

    [Fact]
    public async Task Lease_is_released_after_success()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"));

        await h.RunAsync();

        Assert.Equal([Resource("tenant-a")], h.Leases.Released);
    }

    [Fact]
    public async Task Cursor_is_reread_under_the_lease()
    {
        // Entre o "vencido?" e a tomada do lease, outra réplica terminou um poll deste tenant.
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0), lastPolledAt: Now.AddMinutes(-5));
        h.Cursors.OnGet = call =>
        {
            if (call == 2)
            {
                h.Cursors.Seed("tenant-a", At(12, 9), lastPolledAt: Now);
            }
        };

        await h.RunAsync();

        Assert.Empty(h.Feed.Calls);
        Assert.Equal([Resource("tenant-a")], h.Leases.Released);
    }

    // ---------- fila ----------

    [Fact]
    public async Task References_are_enqueued_with_the_idempotent_trigger()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        var manual = Ref("tenant-a", "A") with { Trigger = IngestionTrigger.Manual };
        h.Feed.Read("tenant-a", new Step(new ChangeFeedPage { References = [manual], HighWatermark = At(12, 1) }));

        await h.RunAsync();

        Assert.Equal(IngestionTrigger.Event, h.Queue.Items.Single().Trigger);
    }

    // ---------- isolamento e registro ----------

    [Fact]
    public async Task One_tenant_failing_does_not_stop_the_other()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled).WithTenant("tenant-c", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        h.Cursors.Seed("tenant-c", At(12, 0));
        h.Feed.Read("tenant-a", Fail(new HttpRequestException("caiu")));
        h.Feed.Read("tenant-c", Page(At(12, 3), "X"));

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Equal(["tenant-c|X"], h.Queue.Keys);
        Assert.Equal(At(12, 3), h.Cursors.Find("tenant-c")!.Watermark);
        Assert.Equal(At(12, 0), h.Cursors.Find("tenant-a")!.Watermark);
        Assert.Equal(1, h.Cursors.Find("tenant-a")!.ConsecutiveFailures);
        Assert.Equal("caiu", summary.Failures["tenant-a"]);
        Assert.Equal(2, summary.TenantsPolled);
    }

    [Fact]
    public async Task Store_failure_on_one_tenant_does_not_stop_the_pass()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled).WithTenant("tenant-c", Enabled);
        h.Cursors.Seed("tenant-c", At(12, 0));
        h.Cursors.FailGetFor = "tenant-a";
        h.Feed.Read("tenant-c", Page(At(12, 3), "X"));

        ChangeFeedPassSummary summary = await h.RunAsync();

        Assert.Equal(["tenant-c|X"], h.Queue.Keys);
        Assert.True(summary.Failures.ContainsKey("tenant-a"));
    }

    [Fact]
    public async Task Success_resets_the_failures()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0), failures: 3, lastError: "caiu", notBefore: Now.AddMinutes(-1));
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"));

        await h.RunAsync();

        ChangeFeedCursor cursor = h.Cursors.Find("tenant-a")!;
        Assert.Equal(0, cursor.ConsecutiveFailures);
        Assert.Null(cursor.LastError);
        Assert.Null(cursor.NotBefore);
        Assert.Equal(Now, cursor.LastPolledAt);
    }

    [Fact]
    public async Task Cancellation_is_not_a_failure()
    {
        var h = new Harness().WithTenant("tenant-a", Enabled);
        h.Cursors.Seed("tenant-a", At(12, 0));
        using var cts = new CancellationTokenSource();
        h.Feed.Read("tenant-a", Page(At(12, 1), "A"), new Step(null, OnReach: cts.Cancel), Page(At(12, 2), "B"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Poller.RunOnceAsync(cts.Token));

        ChangeFeedCursor cursor = h.Cursors.Find("tenant-a")!;
        Assert.Equal(At(12, 1), cursor.Watermark);   // a primeira página ficou gravada
        Assert.Equal(0, cursor.ConsecutiveFailures);
        Assert.Equal([Resource("tenant-a")], h.Leases.Released);
    }

    // ---------- apoio ----------

    private const string Enabled = """{"poll":{"enabled":true}}""";

    private static DateTimeOffset At(int hour, int minute, int second = 0)
        => new(2026, 9, 25, hour, minute, second, TimeSpan.Zero);

    private static string Resource(string tenant) => $"changefeed:{Origin}:{tenant}";

    private static DocumentReference Ref(string tenant, string key) => new()
    {
        TenantId = tenant,
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = key,
        Locator = $"d365/brmf/{key}",
    };

    private static Step Page(DateTimeOffset? highWatermark, params string[] keys)
        => new(new ChangeFeedPage { References = keys.Select(k => Ref("?", k)).ToList(), HighWatermark = highWatermark });

    private static Step Fail(Exception error) => new(null, Error: error);

    /// <summary>Um passo da leitura roteirizada: uma página, uma falha, ou um gancho (ex.: cancelar).</summary>
    private sealed record Step(ChangeFeedPage? Page, Exception? Error = null, Action? OnReach = null);

    private sealed class Harness
    {
        public Harness(int maxPagesPerPass = 20)
        {
            Clock = new StubClock(Now);
            Leases = new FakeLeases(Clock);
            Cursors = new FakeCursors(Leases);
            Poller = new ChangeFeedPoller(
                Feed, Profiles, Cursors, Leases, Queue,
                new ChangeFeedPollerOptions { OwnerId = Owner, MaxPagesPerPass = maxPagesPerPass, LeaseTtl = TimeSpan.FromMinutes(2) },
                Clock);
        }

        public StubClock Clock { get; }
        public FakeFeed Feed { get; } = new();
        public FakeProfiles Profiles { get; } = new();
        public FakeLeases Leases { get; }
        public FakeCursors Cursors { get; }
        public FakeQueue Queue { get; } = new();
        public ChangeFeedPoller Poller { get; }

        public Harness WithTenant(string tenant, string inboundSettings, string inboundAdapter = Origin)
        {
            Profiles.Items.Add(new TenantConnectorProfile
            {
                TenantId = tenant,
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = inboundAdapter,
                InboundSettings = inboundSettings,
                OutboundAdapter = "Avalara",
            });
            return this;
        }

        public Task<ChangeFeedPassSummary> RunAsync() => Poller.RunOnceAsync();
    }

    private sealed class FakeFeed : IDocumentChangeFeed
    {
        private readonly Dictionary<string, Queue<Step[]>> _reads = [];

        public string Origin => ChangeFeedPollerTests.Origin;

        public List<(string Tenant, DateTimeOffset Since)> Calls { get; } = [];

        /// <summary>Roteiriza a próxima leitura do tenant (uma chamada de PullAsync).</summary>
        public void Read(string tenant, params Step[] steps)
        {
            if (!_reads.TryGetValue(tenant, out Queue<Step[]>? queue))
            {
                _reads[tenant] = queue = new Queue<Step[]>();
            }

            queue.Enqueue(steps);
        }

        public async IAsyncEnumerable<ChangeFeedPage> PullAsync(
            string tenantId, DateTimeOffset since, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls.Add((tenantId, since));
            Step[] steps = _reads.TryGetValue(tenantId, out Queue<Step[]>? queue) && queue.Count > 0 ? queue.Dequeue() : [];

            foreach (Step step in steps)
            {
                await Task.Yield();
                step.OnReach?.Invoke();
                ct.ThrowIfCancellationRequested();

                if (step.Error is not null)
                {
                    throw step.Error;
                }

                if (step.Page is not null)
                {
                    // O feed real devolve referências do próprio tenant.
                    yield return step.Page with
                    {
                        References = step.Page.References.Select(r => r.TenantId == "?" ? r with { TenantId = tenantId } : r).ToList(),
                    };
                }
            }
        }
    }

    private sealed class FakeProfiles : IConnectorProfileStore
    {
        public List<TenantConnectorProfile> Items { get; } = [];

        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(p => p.TenantId == tenantId));

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>(Items.Where(p => p.InboundAdapter == inboundAdapter).ToList());
    }

    private sealed class FakeLeases(StubClock clock) : ILeaseStore
    {
        private readonly Dictionary<string, (string Owner, DateTimeOffset Expires)> _held = [];

        public bool RenewFails { get; set; }

        public List<string> Released { get; } = [];

        public void HeldBy(string resource, string owner, DateTimeOffset expires) => _held[resource] = (owner, expires);

        public bool IsHeld(string resource) => _held.ContainsKey(resource);

        public bool IsHeldBy(string resource, string owner)
            => _held.TryGetValue(resource, out var l) && l.Owner == owner && l.Expires > clock.GetUtcNow();

        public Task<bool> TryAcquireAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default)
        {
            if (_held.TryGetValue(resource, out var l) && l.Owner != owner && l.Expires > clock.GetUtcNow())
            {
                return Task.FromResult(false);
            }

            _held[resource] = (owner, clock.GetUtcNow() + ttl);
            return Task.FromResult(true);
        }

        public Task<bool> RenewAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default)
        {
            if (RenewFails || !_held.TryGetValue(resource, out var l) || l.Owner != owner)
            {
                return Task.FromResult(false);
            }

            _held[resource] = (owner, clock.GetUtcNow() + ttl);
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(string resource, string owner, CancellationToken ct = default)
        {
            if (_held.TryGetValue(resource, out var l) && l.Owner == owner)
            {
                _held.Remove(resource);
                Released.Add(resource);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeCursors(FakeLeases leases) : IChangeFeedCursorStore
    {
        private readonly Dictionary<string, ChangeFeedCursor> _items = [];
        private int _gets;

        public List<DateTimeOffset> Advances { get; } = [];

        public bool RefuseAdvance { get; set; }

        public string? FailGetFor { get; set; }

        public Action<int>? OnGet { get; set; }

        public ChangeFeedCursor? Find(string tenant) => _items.GetValueOrDefault(tenant);

        public void Seed(
            string tenant, DateTimeOffset? watermark, DateTimeOffset? lastPolledAt = null,
            DateTimeOffset? notBefore = null, int failures = 0, string? lastError = null)
            => _items[tenant] = new ChangeFeedCursor
            {
                TenantId = tenant,
                Origin = Origin,
                Watermark = watermark,
                LastPolledAt = lastPolledAt,
                NotBefore = notBefore,
                ConsecutiveFailures = failures,
                LastError = lastError,
            };

        public Task<ChangeFeedCursor?> GetAsync(string tenantId, string origin, CancellationToken ct = default)
        {
            if (tenantId == FailGetFor)
            {
                throw new InvalidOperationException("banco fora");
            }

            OnGet?.Invoke(++_gets);
            return Task.FromResult(Find(tenantId));
        }

        public Task<ChangeFeedCursor> StartAsync(string tenantId, string origin, DateTimeOffset initialWatermark, CancellationToken ct = default)
        {
            ChangeFeedCursor? current = Find(tenantId);
            if (current?.Watermark is null)
            {
                _items[tenantId] = current = (current ?? new ChangeFeedCursor { TenantId = tenantId, Origin = origin }) with
                {
                    Watermark = initialWatermark,
                };
            }

            return Task.FromResult(current);
        }

        public Task<bool> TryAdvanceWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
        {
            ChangeFeedCursor current = _items[tenantId];
            if (RefuseAdvance || !leases.IsHeldBy(lease.Resource, lease.Owner) || !(current.Watermark < watermark))
            {
                return Task.FromResult(false);
            }

            _items[tenantId] = current with { Watermark = watermark };
            Advances.Add(watermark);
            return Task.FromResult(true);
        }

        public Task RecordSuccessAsync(string tenantId, string origin, DateTimeOffset polledAt, CancellationToken ct = default)
        {
            _items[tenantId] = _items[tenantId] with { LastPolledAt = polledAt, ConsecutiveFailures = 0, LastError = null, NotBefore = null };
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(string tenantId, string origin, DateTimeOffset polledAt, string error, DateTimeOffset? notBefore, CancellationToken ct = default)
        {
            ChangeFeedCursor current = Find(tenantId) ?? new ChangeFeedCursor { TenantId = tenantId, Origin = origin };
            _items[tenantId] = current with
            {
                LastPolledAt = polledAt,
                ConsecutiveFailures = current.ConsecutiveFailures + 1,
                LastError = error,
                NotBefore = notBefore,
            };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueue : IDocumentQueue
    {
        public List<DocumentReference> Items { get; } = [];

        public string? FailOn { get; set; }

        public IEnumerable<string> Keys => Items.Select(r => $"{r.TenantId}|{r.NaturalKey}");

        public Task EnqueueAsync(DocumentReference reference, CancellationToken ct = default)
        {
            if ($"{reference.TenantId}|{reference.NaturalKey}" == FailOn)
            {
                throw new InvalidOperationException("fila fora");
            }

            Items.Add(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
