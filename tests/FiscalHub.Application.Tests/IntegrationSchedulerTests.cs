using FiscalHub.Application.Integrations;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o agendador: calcula o período (D-1 no diário, explícito no único), dispara pelo
/// runner e reprograma (diário +1 dia; único desativa).
/// </summary>
public class IntegrationSchedulerTests
{
    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    [Fact]
    public async Task Daily_runs_previous_day_and_advances_one_day()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var runAt = new DateTimeOffset(2026, 7, 24, 6, 0, 0, Brt);   // vencido
        var store = new FakeScheduleStore(new ScheduledIntegration
        {
            Id = 1,
            Mode = IntegrationMode.ScheduledDaily,
            TenantId = "tenant-a",
            CompanyCode = "12345678",
            NextRunAt = runAt,
        });
        var runner = new FakeRunner();
        var scheduler = new IntegrationScheduler(store, runner, new StubClock(now));

        int ran = await scheduler.RunDueAsync();

        Assert.Equal(1, ran);
        Assert.Equal(IntegrationMode.ScheduledDaily, runner.Last!.Mode);
        Assert.Equal(new DateOnly(2026, 7, 23), DateOnly.FromDateTime(runner.Last.PeriodStart.ToOffset(Brt).Date)); // D-1
        Assert.Equal(new DateOnly(2026, 7, 23), DateOnly.FromDateTime(runner.Last.PeriodEnd.ToOffset(Brt).Date));
        Assert.Equal(runAt.AddDays(1), store.Single.NextRunAt);   // reprogramou +1 dia
        Assert.True(store.Single.Active);
    }

    [Fact]
    public async Task Once_runs_explicit_period_and_deactivates()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeScheduleStore(new ScheduledIntegration
        {
            Id = 2,
            Mode = IntegrationMode.ScheduledOnce,
            TenantId = "tenant-a",
            CompanyCode = "98765432",
            PeriodStart = "2026-06-01",
            PeriodEnd = "2026-06-30",
            NextRunAt = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
        });
        var runner = new FakeRunner();
        var scheduler = new IntegrationScheduler(store, runner, new StubClock(now));

        await scheduler.RunDueAsync();

        Assert.Equal(new DateOnly(2026, 6, 1), DateOnly.FromDateTime(runner.Last!.PeriodStart.ToOffset(Brt).Date));
        Assert.Equal(new DateOnly(2026, 6, 30), DateOnly.FromDateTime(runner.Last.PeriodEnd.ToOffset(Brt).Date));
        Assert.False(store.Single.Active);   // único: cumpriu, desativou
    }

    [Fact]
    public async Task Not_yet_due_is_left_alone()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeScheduleStore(new ScheduledIntegration
        {
            Id = 3,
            Mode = IntegrationMode.ScheduledDaily,
            TenantId = "tenant-a",
            CompanyCode = "12345678",
            NextRunAt = now.AddHours(2),   // ainda não venceu
        });
        var runner = new FakeRunner();
        var scheduler = new IntegrationScheduler(store, runner, new StubClock(now));

        int ran = await scheduler.RunDueAsync();

        Assert.Equal(0, ran);
        Assert.Null(runner.Last);
    }

    private sealed class StubClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRunner : IIntegrationRunner
    {
        public RunRequest? Last { get; private set; }

        public Task<int> RunAsync(RunRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeScheduleStore : IScheduleStore
    {
        private readonly List<ScheduledIntegration> _items;

        public FakeScheduleStore(params ScheduledIntegration[] items) => _items = [.. items];

        public ScheduledIntegration Single => _items[0];

        public Task<int> CreateAsync(ScheduledIntegration s, CancellationToken ct = default)
        {
            _items.Add(s);
            return Task.FromResult(s.Id);
        }

        public Task<bool> UpdateAsync(ScheduledIntegration s, CancellationToken ct = default)
        {
            int i = _items.FindIndex(x => x.Id == s.Id);
            if (i < 0)
            {
                return Task.FromResult(false);
            }

            _items[i] = s with { Active = true };
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ScheduledIntegration>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScheduledIntegration>>(_items);

        public Task<IReadOnlyList<ScheduledIntegration>> ListDueAsync(DateTimeOffset now, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScheduledIntegration>>(
                _items.Where(s => s.Active && s.NextRunAt <= now).ToList());

        public Task RescheduleAsync(int id, DateTimeOffset? nextRunAt, CancellationToken ct = default)
        {
            int i = _items.FindIndex(s => s.Id == id);
            if (i >= 0)
            {
                _items[i] = _items[i] with { NextRunAt = nextRunAt ?? _items[i].NextRunAt, Active = nextRunAt is not null };
            }

            return Task.CompletedTask;
        }

        public Task DeactivateAsync(int id, CancellationToken ct = default)
        {
            int i = _items.FindIndex(s => s.Id == id);
            if (i >= 0)
            {
                _items[i] = _items[i] with { Active = false };
            }

            return Task.CompletedTask;
        }
    }
}
