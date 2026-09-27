using System.Text.Json;

namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Cache de cadastros (design D13): por (tenant, entidade, RecId), expiração absoluta, invalidação só por
/// tempo; não encontrado não fica em cache; RecId 0 (FK vazia) nem consulta.
/// </summary>
public class D365ReferenceDataCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Hit_within_the_expiration_does_not_call_the_loader()
    {
        var (cache, _) = Cache();
        var loader = new CountingLoader();

        await cache.GetAsync("tenant-a", "FSAddressCityBRs", 22565694955, loader.Load, default);
        JsonElement? second = await cache.GetAsync("tenant-a", "FSAddressCityBRs", 22565694955, loader.Load, default);

        Assert.Equal(1, loader.Calls);
        Assert.Equal("3550308", second!.Value.GetProperty("IBGECode").GetString());
    }

    [Fact]
    public async Task After_the_expiration_the_loader_is_called_again()
    {
        var (cache, clock) = Cache();
        var loader = new CountingLoader();

        await cache.GetAsync("tenant-a", "FSAddressCityBRs", 22565694955, loader.Load, default);
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        await cache.GetAsync("tenant-a", "FSAddressCityBRs", 22565694955, loader.Load, default);

        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task Tenants_do_not_share_entries()
    {
        var (cache, _) = Cache();
        var loader = new CountingLoader();

        await cache.GetAsync("tenant-a", "FSPostalAddressBRs", 22565429296, loader.Load, default);
        await cache.GetAsync("tenant-c", "FSPostalAddressBRs", 22565429296, loader.Load, default);

        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task Not_found_is_not_cached()
    {
        var (cache, _) = Cache();
        var loader = new CountingLoader { Found = false };

        Assert.Null(await cache.GetAsync("tenant-a", "FSPostalAddressBRs", 1, loader.Load, default));
        Assert.Null(await cache.GetAsync("tenant-a", "FSPostalAddressBRs", 1, loader.Load, default));

        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task Empty_foreign_key_does_not_call_the_loader()
    {
        var (cache, _) = Cache();
        var loader = new CountingLoader();

        Assert.Null(await cache.GetAsync("tenant-a", "FSAddressCityBRs", 0, loader.Load, default));

        Assert.Equal(0, loader.Calls);
    }

    private static (D365ReferenceDataCache Cache, StepClock Clock) Cache()
    {
        var clock = new StepClock(Now);
        return (new D365ReferenceDataCache(new D365AssemblyOptions(), clock), clock);
    }

    private sealed class CountingLoader
    {
        public int Calls { get; private set; }

        public bool Found { get; init; } = true;

        public Task<JsonElement?> Load(CancellationToken ct)
        {
            Calls++;
            if (!Found)
            {
                return Task.FromResult<JsonElement?>(null);
            }

            using JsonDocument doc = JsonDocument.Parse("""{"AddressCityRecId":22565694955,"IBGECode":"3550308"}""");
            return Task.FromResult<JsonElement?>(doc.RootElement.Clone());
        }
    }

    private sealed class StepClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
