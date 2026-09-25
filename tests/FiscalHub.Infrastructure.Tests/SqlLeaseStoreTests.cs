using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>Especifica o lease em SQL: aquisição, disputa, expiração, renovação e liberação por dono.</summary>
public class SqlLeaseStoreTests
{
    private const string Resource = "changefeed:Dynamics365:tenant-a";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Acquires_a_free_lease()
    {
        using var h = new Harness();

        Assert.True(await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl));
    }

    [Fact]
    public async Task Second_owner_is_refused_while_the_lease_is_valid()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        // Outro contexto sobre o mesmo banco: é outra réplica, não o mesmo DbContext.
        Assert.False(await h.OtherStore().TryAcquireAsync(Resource, "replica-2", Ttl));
    }

    [Fact]
    public async Task Expired_lease_can_be_taken_by_another_owner()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        h.Clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        Assert.True(await h.OtherStore().TryAcquireAsync(Resource, "replica-2", Ttl));
    }

    [Fact]
    public async Task Owner_can_reacquire_its_own_lease()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        Assert.True(await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl));
    }

    [Fact]
    public async Task Renewal_by_the_owner_extends_the_deadline()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        h.Clock.Advance(TimeSpan.FromMinutes(1.5));
        Assert.True(await h.Store.RenewAsync(Resource, "replica-1", Ttl));
        h.Clock.Advance(TimeSpan.FromMinutes(1));   // passou do prazo original, não do renovado

        Assert.False(await h.OtherStore().TryAcquireAsync(Resource, "replica-2", Ttl));
    }

    [Fact]
    public async Task Renewal_by_someone_else_is_refused()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        Assert.False(await h.OtherStore().RenewAsync(Resource, "replica-2", Ttl));
        Assert.False(await h.Store.RenewAsync("changefeed:Dynamics365:inexistente", "replica-1", Ttl));
    }

    [Fact]
    public async Task Renewal_after_another_owner_took_the_lease_is_refused()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);
        h.Clock.Advance(Ttl + TimeSpan.FromSeconds(1));
        await h.OtherStore().TryAcquireAsync(Resource, "replica-2", Ttl);

        Assert.False(await h.Store.RenewAsync(Resource, "replica-1", Ttl));
    }

    [Fact]
    public async Task Release_only_by_the_owner()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        await h.OtherStore().ReleaseAsync(Resource, "replica-2");   // não é dele: não solta
        Assert.False(await h.OtherStore().TryAcquireAsync(Resource, "replica-2", Ttl));

        await h.Store.ReleaseAsync(Resource, "replica-1");
        Assert.True(await h.OtherStore().TryAcquireAsync(Resource, "replica-2", Ttl));
    }

    [Fact]
    public async Task Leases_are_independent_per_resource()
    {
        using var h = new Harness();
        await h.Store.TryAcquireAsync(Resource, "replica-1", Ttl);

        Assert.True(await h.OtherStore().TryAcquireAsync("changefeed:Dynamics365:tenant-c", "replica-2", Ttl));
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
            Store = new SqlLeaseStore(db, Clock);
        }

        public StubClock Clock { get; } = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));

        public SqlLeaseStore Store { get; }

        public SqlLeaseStore OtherStore() => new(NewContext(), Clock);

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

/// <summary>Relógio controlável para os testes de store.</summary>
internal sealed class StubClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}
