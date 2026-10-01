using FiscalHub.Application.Coordination;
using FiscalHub.Application.Inbound;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o cursor do feed de mudanças: marca inicial, ticks exatos, avanço condicionado ao lease
/// (fencing) e monotônico, registro de sucesso/falha e isolamento por (tenant, origem).
/// </summary>
public class SqlChangeFeedCursorStoreTests
{
    private const string Origin = "Dynamics365";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private static readonly LeaseClaim Claim = new("changefeed:Dynamics365:tenant-a", "replica-1");

    // Com fração de segundo: prova que o tick volta exato, não arredondado.
    private static readonly DateTimeOffset Start = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    [Fact]
    public async Task Start_creates_the_cursor_with_the_initial_watermark_and_exact_ticks()
    {
        using var h = new Harness();

        ChangeFeedCursor started = await h.Store.StartAsync("tenant-a", Origin, Start);
        ChangeFeedCursor? read = await h.OtherStore().GetAsync("tenant-a", Origin);

        Assert.Equal(Start, started.Watermark);
        Assert.Equal(Start.UtcTicks, read!.Watermark!.Value.UtcTicks);
        Assert.Equal(TimeSpan.Zero, read.Watermark.Value.Offset);
    }

    [Fact]
    public async Task Start_does_not_overwrite_an_existing_watermark()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);

        ChangeFeedCursor again = await h.Store.StartAsync("tenant-a", Origin, Start.AddYears(10));

        Assert.Equal(Start, again.Watermark);
    }

    [Fact]
    public async Task Start_fills_the_watermark_of_a_cursor_created_by_a_failure()
    {
        using var h = new Harness();
        await h.Store.RecordFailureAsync("tenant-a", Origin, h.Clock.GetUtcNow(), "settings inválidas", notBefore: null);
        Assert.Null((await h.Store.GetAsync("tenant-a", Origin))!.Watermark);

        ChangeFeedCursor started = await h.Store.StartAsync("tenant-a", Origin, Start);

        Assert.Equal(Start, started.Watermark);
        Assert.Equal(1, started.ConsecutiveFailures);   // não apaga o histórico de falha
    }

    [Fact]
    public async Task Advance_with_the_valid_lease_of_the_owner_is_accepted()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        await h.Leases.TryAcquireAsync(Claim.Resource, Claim.Owner, Ttl);

        Assert.True(await h.Store.TryAdvanceWatermarkAsync("tenant-a", Origin, Start.AddDays(1), Claim));
        Assert.Equal(Start.AddDays(1), (await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);
    }

    [Fact]
    public async Task Advance_with_the_lease_of_another_owner_is_refused()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        await h.Leases.TryAcquireAsync(Claim.Resource, "replica-2", Ttl);

        Assert.False(await h.Store.TryAdvanceWatermarkAsync("tenant-a", Origin, Start.AddDays(1), Claim));
        Assert.Equal(Start, (await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);
    }

    [Fact]
    public async Task Advance_with_an_expired_lease_is_refused()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        await h.Leases.TryAcquireAsync(Claim.Resource, Claim.Owner, Ttl);
        h.Clock.Advance(Ttl + TimeSpan.FromSeconds(1));   // pausa longa: o lease venceu

        Assert.False(await h.Store.TryAdvanceWatermarkAsync("tenant-a", Origin, Start.AddDays(1), Claim));
    }

    [Fact]
    public async Task Advance_without_any_lease_is_refused()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);

        Assert.False(await h.Store.TryAdvanceWatermarkAsync("tenant-a", Origin, Start.AddDays(1), Claim));
    }

    [Fact]
    public async Task Advance_that_is_not_greater_is_refused()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        await h.Leases.TryAcquireAsync(Claim.Resource, Claim.Owner, Ttl);

        Assert.False(await h.Store.TryAdvanceWatermarkAsync("tenant-a", Origin, Start, Claim));
        Assert.False(await h.Store.TryAdvanceWatermarkAsync("tenant-a", Origin, Start.AddDays(-1), Claim));
        Assert.Equal(Start, (await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);
    }

    // ---- Rebobinar: o mesmo fencing do avanço, com a comparação invertida (change module-navigation-and-integration-panel, D7) ----

    private static readonly LeaseClaim Rewind = new("changefeed:Dynamics365:tenant-a", "rewind:1");

    [Fact]
    public async Task Rewind_with_the_valid_lease_of_the_owner_and_a_lower_target_is_accepted()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start.AddYears(10));
        await h.Leases.TryAcquireAsync(Rewind.Resource, Rewind.Owner, Ttl);

        Assert.True(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind));
        Assert.Equal(Start, (await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);   // ticks exatos
    }

    [Fact]
    public async Task Rewind_that_is_not_lower_is_refused()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        await h.Leases.TryAcquireAsync(Rewind.Resource, Rewind.Owner, Ttl);

        Assert.False(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind));
        Assert.False(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start.AddDays(1), Rewind));
        Assert.Equal(Start, (await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);
    }

    [Fact]
    public async Task Rewind_with_the_lease_of_another_owner_or_expired_is_refused()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start.AddYears(10));
        await h.Leases.TryAcquireAsync(Rewind.Resource, "replica-1", Ttl);   // o coletor está lendo

        Assert.False(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind));

        await h.Leases.ReleaseAsync(Rewind.Resource, "replica-1");
        await h.Leases.TryAcquireAsync(Rewind.Resource, Rewind.Owner, Ttl);
        h.Clock.Advance(Ttl + TimeSpan.FromSeconds(1));   // o lease do rebobinamento venceu

        Assert.False(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind));
        Assert.Equal(Start.AddYears(10), (await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);
    }

    [Fact]
    public async Task Rewind_without_the_row_writes_nothing_and_creates_nothing()
    {
        using var h = new Harness();
        await h.Leases.TryAcquireAsync(Rewind.Resource, Rewind.Owner, Ttl);

        Assert.False(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind));
        Assert.Null(await h.OtherStore().GetAsync("tenant-a", Origin));
    }

    [Fact]
    public async Task Rewind_of_a_cursor_without_mark_writes_nothing()
    {
        using var h = new Harness();
        await h.Store.RecordFailureAsync("tenant-a", Origin, h.Clock.GetUtcNow(), "falhou antes da primeira passada", notBefore: null);
        await h.Leases.TryAcquireAsync(Rewind.Resource, Rewind.Owner, Ttl);

        Assert.False(await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind));
        Assert.Null((await h.OtherStore().GetAsync("tenant-a", Origin))!.Watermark);
    }

    [Fact]
    public async Task Rewind_keeps_the_diagnostic_of_the_collector()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start.AddYears(10));
        DateTimeOffset polled = h.Clock.GetUtcNow();
        await h.Store.RecordFailureAsync("tenant-a", Origin, polled, "AADSTS7000215", notBefore: polled.AddMinutes(5));
        await h.Leases.TryAcquireAsync(Rewind.Resource, Rewind.Owner, Ttl);

        await h.Store.TryRewindWatermarkAsync("tenant-a", Origin, Start, Rewind);

        ChangeFeedCursor rewound = (await h.OtherStore().GetAsync("tenant-a", Origin))!;
        Assert.Equal(Start, rewound.Watermark);
        Assert.Equal(polled, rewound.LastPolledAt);
        Assert.Equal(1, rewound.ConsecutiveFailures);
        Assert.Equal("AADSTS7000215", rewound.LastError);
        Assert.Equal(polled.AddMinutes(5), rewound.NotBefore);
    }

    [Fact]
    public async Task Records_failure_then_success()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        DateTimeOffset t1 = h.Clock.GetUtcNow();

        await h.Store.RecordFailureAsync("tenant-a", Origin, t1, new string('x', 800), notBefore: t1.AddMinutes(10));
        await h.Store.RecordFailureAsync("tenant-a", Origin, t1.AddMinutes(1), "caiu de novo", notBefore: null);
        ChangeFeedCursor failed = (await h.OtherStore().GetAsync("tenant-a", Origin))!;
        Assert.Equal(2, failed.ConsecutiveFailures);
        Assert.Equal("caiu de novo", failed.LastError);
        Assert.Null(failed.NotBefore);
        Assert.Equal(t1.AddMinutes(1), failed.LastPolledAt);

        await h.Store.RecordFailureAsync("tenant-a", Origin, t1, new string('x', 800), notBefore: t1.AddMinutes(10));
        ChangeFeedCursor truncated = (await h.OtherStore().GetAsync("tenant-a", Origin))!;
        Assert.Equal(500, truncated.LastError!.Length);
        Assert.Equal(t1.AddMinutes(10), truncated.NotBefore);

        await h.Store.RecordSuccessAsync("tenant-a", Origin, t1.AddMinutes(2));
        ChangeFeedCursor ok = (await h.OtherStore().GetAsync("tenant-a", Origin))!;
        Assert.Equal(0, ok.ConsecutiveFailures);
        Assert.Null(ok.LastError);
        Assert.Null(ok.NotBefore);
        Assert.Equal(t1.AddMinutes(2), ok.LastPolledAt);
        Assert.Equal(Start, ok.Watermark);   // registrar o poll não mexe na marca
    }

    [Fact]
    public async Task Cursors_are_isolated_by_tenant_and_origin()
    {
        using var h = new Harness();
        await h.Store.StartAsync("tenant-a", Origin, Start);
        await h.Store.StartAsync("tenant-c", Origin, Start.AddYears(1));
        await h.Store.StartAsync("tenant-a", "OutroErp", Start.AddYears(2));

        Assert.Equal(Start, (await h.Store.GetAsync("tenant-a", Origin))!.Watermark);
        Assert.Equal(Start.AddYears(1), (await h.Store.GetAsync("tenant-c", Origin))!.Watermark);
        Assert.Equal(Start.AddYears(2), (await h.Store.GetAsync("tenant-a", "OutroErp"))!.Watermark);
        Assert.Null(await h.Store.GetAsync("tenant-b", Origin));
    }

    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _conn = new("DataSource=:memory:");
        private readonly List<ProcessingDbContext> _contexts = [];

        public Harness()
        {
            _conn.Open();
            ProcessingDbContext db = NewContext();
            db.Database.EnsureCreated();
            Store = new SqlChangeFeedCursorStore(db, Clock);
            Leases = new SqlLeaseStore(NewContext(), Clock);
        }

        public StubClock Clock { get; } = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));

        public SqlChangeFeedCursorStore Store { get; }

        public SqlLeaseStore Leases { get; }

        /// <summary>Store sobre um contexto novo: lê o que está no banco, não o que o change tracker lembra.</summary>
        public SqlChangeFeedCursorStore OtherStore() => new(NewContext(), Clock);

        private ProcessingDbContext NewContext()
        {
            var db = new ProcessingDbContext(new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(_conn).Options);
            _contexts.Add(db);
            return db;
        }

        public void Dispose()
        {
            _contexts.ForEach(c => c.Dispose());
            _conn.Dispose();
        }
    }
}
