using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o resolvedor do estabelecimento pela plataforma (spec platform-establishment-resolution, design D2, D4 e
/// D8): o casamento pelo CNPJ normalizado, a duplicidade contada pelo identificador do contribuinte, sem desempate, e a
/// janela — uma listagem por tenant e ambiente dentro da validade, uma busca só sob concorrência, a recusa lembrada e o
/// salvar do perfil que esquece. A listagem falsa conta as chamadas: o resultado certo com N chamadas é o defeito.
/// </summary>
public class PlatformEstablishmentResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    // --- O casamento (1.1) ---

    [Fact]
    public async Task One_taxpayer_is_unique_and_known()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        var unique = Assert.IsType<EstablishmentMatch.Unique>(index.Match("44278225000180"));
        Assert.Equal(("005", "010"), (unique.Establishment.CompanyCode, unique.Establishment.EstablishmentCode));
        Assert.True(index.Knows("44278225000180"));
        Assert.True(index.CanList);
    }

    [Fact]
    public async Task No_taxpayer_is_none_and_unknown()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.IsType<EstablishmentMatch.None>(index.Match("44278225000341"));
        Assert.False(index.Knows("44278225000341"));
    }

    [Fact]
    public async Task Two_taxpayers_in_two_companies_are_ambiguous_even_with_the_same_codes()
    {
        var h = new Harness([
            Taxpayer("44278225000180", "005", "001", "2000010001"),
            Taxpayer("44278225000180", "005", "001", "20001"),   // os mesmos códigos, outra empresa: duas escriturações
        ]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        var ambiguous = Assert.IsType<EstablishmentMatch.Ambiguous>(index.Match("44278225000180"));
        Assert.Equal(["2000010001", "20001"], ambiguous.Candidates.Select(c => c.PlatformId));
        Assert.True(index.Knows("44278225000180"));
    }

    [Fact]
    public async Task The_same_taxpayer_listed_twice_is_one_candidate()
    {
        var h = new Harness([
            Taxpayer("44278225000180", "005", "010", "2000010001"),
            Taxpayer("44278225000180", "005", "010", "2000010001"),
        ]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.IsType<EstablishmentMatch.Unique>(index.Match("44278225000180"));
    }

    [Fact]
    public async Task Two_rows_without_identifier_are_two_candidates()
    {
        // Sem identificador, cada linha conta à parte: o erro, se houver, vai para o lado de recusar mais (D4).
        var h = new Harness([
            Taxpayer("44278225000180", "005", "010", platformId: null),
            Taxpayer("44278225000180", "005", "010", platformId: null),
        ]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.Equal(2, Assert.IsType<EstablishmentMatch.Ambiguous>(index.Match("44278225000180")).Candidates.Count);
    }

    [Fact]
    public async Task The_code_that_matches_the_cnpj_order_does_not_break_the_tie()
    {
        // 44278225000260 é a ordem 0002: o "002" coincidir com ela é convenção de alguns clientes, e não regra.
        var h = new Harness([
            Taxpayer("44278225000260", "005", "002", "2000010002"),
            Taxpayer("44278225000260", "QA", "007", "20007"),
        ]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.IsType<EstablishmentMatch.Ambiguous>(index.Match("44278225000260"));
    }

    [Fact]
    public async Task The_erp_cnpj_with_a_hyphen_matches_the_platform_digits()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.IsType<EstablishmentMatch.Unique>(index.Match("442782250001-80"));
    }

    [Fact]
    public async Task Alphanumeric_cnpj_matches_with_the_same_value_on_both_ends()
    {
        var h = new Harness([Taxpayer("12.ABC.345/01DE-35", "005", "ALFA", "2000010009")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        var unique = Assert.IsType<EstablishmentMatch.Unique>(index.Match("12.ABC.345/01DE-35"));
        Assert.Equal("12ABC34501DE35", unique.Establishment.TaxId);
        Assert.IsType<EstablishmentMatch.Unique>(index.Match("12ABC34501DE35"));
    }

    [Fact]
    public async Task A_different_case_does_not_match()
    {
        var h = new Harness([Taxpayer("12abc34501de35", "005", "ALFA", "2000010009")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.IsType<EstablishmentMatch.None>(index.Match("12ABC34501DE35"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("./-")]
    public async Task A_taxpayer_without_cnpj_is_left_out(string cnpj)
    {
        var h = new Harness([Taxpayer(cnpj, "005", "010", "2000010001")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.IsType<EstablishmentMatch.None>(index.Match(cnpj));
        Assert.False(index.Knows(""));
    }

    // --- A janela (1.2) ---

    [Fact]
    public async Task N_resolutions_make_one_call()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        for (int i = 0; i < 50; i++)
        {
            await h.Resolver.GetAsync(Profile());
        }

        Assert.Equal(1, h.Listing.Calls);
    }

    [Fact]
    public async Task Concurrent_resolutions_make_one_call_and_share_the_index()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        h.Listing.Hold();

        Task<PlatformEstablishmentIndex>[] resolutions = [.. Enumerable.Range(0, 10).Select(_ => h.Resolver.GetAsync(Profile()))];
        h.Listing.Release();
        PlatformEstablishmentIndex[] indexes = await Task.WhenAll(resolutions);

        Assert.Equal(1, h.Listing.Calls);
        Assert.All(indexes, i => Assert.Same(indexes[0], i));
    }

    [Fact]
    public async Task Concurrent_resolutions_share_the_failure_too()
    {
        var h = new Harness([]);
        h.Listing.Hold();

        Task<PlatformEstablishmentIndex>[] resolutions = [.. Enumerable.Range(0, 10).Select(_ => h.Resolver.GetAsync(Profile()))];
        h.Listing.Release(new HttpRequestException("503"));

        foreach (Task<PlatformEstablishmentIndex> resolution in resolutions)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => resolution);
        }

        Assert.Equal(1, h.Listing.Calls);
    }

    [Fact]
    public async Task After_the_validity_the_platform_is_listed_again()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        await h.Resolver.GetAsync(Profile());
        h.Clock.Advance(TimeSpan.FromMinutes(9));
        await h.Resolver.GetAsync(Profile());
        Assert.Equal(1, h.Listing.Calls);

        h.Clock.Advance(TimeSpan.FromMinutes(1));   // 10 minutos: a validade padrão venceu
        await h.Resolver.GetAsync(Profile());

        Assert.Equal(2, h.Listing.Calls);
    }

    [Fact]
    public async Task Two_tenants_never_share_a_listing()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        await h.Resolver.GetAsync(Profile("tenant-a"));
        await h.Resolver.GetAsync(Profile("tenant-b"));
        await h.Resolver.GetAsync(Profile("tenant-a"));

        Assert.Equal(["tenant-a", "tenant-b"], h.Listing.Tenants);
    }

    [Fact]
    public async Task Two_environments_of_the_same_tenant_never_share_a_listing()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        await h.Resolver.GetAsync(Profile(environment: "Sandbox"));
        await h.Resolver.GetAsync(Profile(environment: "Production"));
        await h.Resolver.GetAsync(Profile(environment: "SANDBOX"));   // o ambiente, em minúsculas, é a chave

        Assert.Equal(2, h.Listing.Calls);
    }

    [Fact]
    public async Task A_transient_failure_is_not_kept()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        h.Listing.FailNext(new HttpRequestException("503"));

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Resolver.GetAsync(Profile()));
        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.Equal(2, h.Listing.Calls);
        Assert.True(index.Knows("44278225000180"));
    }

    [Fact]
    public async Task A_refusal_is_remembered_within_the_hold_with_the_same_reason()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        h.Listing.FailNext(new DispatchRejectedException("Configuração do conector: a plataforma negou a listagem (HTTP 403)."));

        var first = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Resolver.GetAsync(Profile()));
        for (int i = 0; i < 20; i++)
        {
            var again = await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Resolver.GetAsync(Profile()));
            Assert.Equal(first.Reason, again.Reason);
        }

        Assert.Equal(1, h.Listing.Calls);

        h.Clock.Advance(TimeSpan.FromMinutes(5));   // o intervalo padrão passou: uma chamada nova
        await h.Resolver.GetAsync(Profile());

        Assert.Equal(2, h.Listing.Calls);
    }

    [Fact]
    public async Task Saving_the_profile_forgets_the_listing_of_the_tenant_in_every_environment()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        await h.Resolver.GetAsync(Profile(environment: "Sandbox"));
        await h.Resolver.GetAsync(Profile(environment: "Production"));

        await h.Resolver.ProfileSavedAsync("tenant-a");
        await h.Resolver.GetAsync(Profile(environment: "Sandbox"));
        await h.Resolver.GetAsync(Profile(environment: "Production"));

        Assert.Equal(4, h.Listing.Calls);
    }

    [Fact]
    public async Task Saving_the_profile_forgets_the_remembered_refusal()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        h.Listing.FailNext(new DispatchRejectedException("Configuração do conector: recusa."));
        await Assert.ThrowsAsync<DispatchRejectedException>(() => h.Resolver.GetAsync(Profile()));

        await h.Resolver.ProfileSavedAsync("tenant-a");
        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile());

        Assert.True(index.Knows("44278225000180"));
        Assert.Equal(2, h.Listing.Calls);
    }

    [Fact]
    public async Task Saving_the_profile_forgets_the_listing_in_flight()
    {
        // A listagem em voo começou antes do salvar: ela não pode virar a listagem guardada, e quem chega depois lista de
        // novo, já com o que foi salvo.
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        h.Listing.Hold();
        Task<PlatformEstablishmentIndex> before = h.Resolver.GetAsync(Profile());

        await h.Resolver.ProfileSavedAsync("tenant-a");
        h.Listing.Release();
        await before;
        await h.Resolver.GetAsync(Profile());

        Assert.Equal(2, h.Listing.Calls);
    }

    [Fact]
    public async Task Saving_the_profile_does_not_touch_other_tenants()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        await h.Resolver.GetAsync(Profile("tenant-a"));
        await h.Resolver.GetAsync(Profile("tenant-b"));

        await h.Resolver.ProfileSavedAsync("tenant-a");
        await h.Resolver.GetAsync(Profile("tenant-b"));

        Assert.Equal(2, h.Listing.Calls);
    }

    [Fact]
    public async Task An_adapter_without_listing_cannot_list_and_makes_no_call()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile(outboundAdapter: "ThomsonReuters"));

        Assert.False(index.CanList);
        Assert.False(index.Knows("44278225000180"));
        Assert.IsType<EstablishmentMatch.None>(index.Match("44278225000180"));
        Assert.Equal(0, h.Listing.Calls);
    }

    [Fact]
    public async Task The_adapter_name_is_compared_exactly()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);

        PlatformEstablishmentIndex index = await h.Resolver.GetAsync(Profile(outboundAdapter: "avalara"));

        Assert.False(index.CanList);
    }

    [Fact]
    public async Task Cancelling_the_one_who_started_the_listing_does_not_cancel_the_others()
    {
        var h = new Harness([Taxpayer("44278225000180", "005", "010", "2000010001")]);
        h.Listing.Hold();
        using var cts = new CancellationTokenSource();

        Task<PlatformEstablishmentIndex> first = h.Resolver.GetAsync(Profile(), cts.Token);
        Task<PlatformEstablishmentIndex> second = h.Resolver.GetAsync(Profile());
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        h.Listing.Release();

        Assert.True((await second).Knows("44278225000180"));
        Assert.Equal(1, h.Listing.Calls);
        Assert.False(h.Listing.LastToken.CanBeCanceled);   // a busca não leva o token de quem a começou
    }

    // --- As opções (1.3) ---

    [Fact]
    public void Options_default_to_ten_minutes_of_validity_and_five_of_refusal_hold()
    {
        var options = new PlatformEstablishmentOptions();

        Assert.Equal(TimeSpan.FromMinutes(10), options.CacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), options.RefusalHold);
        options.Validate();
    }

    [Theory]
    [InlineData(0, 5, "PlatformEstablishments:CacheDuration")]
    [InlineData(-1, 5, "PlatformEstablishments:CacheDuration")]
    [InlineData(10, 0, "PlatformEstablishments:RefusalHold")]
    [InlineData(10, -1, "PlatformEstablishments:RefusalHold")]
    public void Zero_or_negative_is_refused_naming_the_setting(int cacheMinutes, int holdMinutes, string setting)
    {
        var options = new PlatformEstablishmentOptions
        {
            CacheDuration = TimeSpan.FromMinutes(cacheMinutes),
            RefusalHold = TimeSpan.FromMinutes(holdMinutes),
        };

        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(setting, ex.Message);
        Assert.Throws<InvalidOperationException>(() => new PlatformEstablishmentResolver([], options, TimeProvider.System));
    }

    private static TenantConnectorProfile Profile(string tenant = "tenant-a", string environment = "Sandbox", string outboundAdapter = "Avalara") => new()
    {
        TenantId = tenant,
        Environment = environment,
        InboundAdapter = "Dynamics365",
        OutboundAdapter = outboundAdapter,
    };

    private static PlatformEstablishment Taxpayer(string taxId, string? company, string? code, string? platformId)
        => new(taxId, company, code, platformId, CompanyName: null);

    private sealed class Harness
    {
        public Harness(PlatformEstablishment[] establishments)
        {
            Listing = new FakeListing(establishments);
            Resolver = new PlatformEstablishmentResolver([Listing], new PlatformEstablishmentOptions(), Clock);
        }

        public StubClock Clock { get; } = new(Now);

        public FakeListing Listing { get; }

        public PlatformEstablishmentResolver Resolver { get; }
    }

    private sealed class FakeListing(PlatformEstablishment[] establishments) : IPlatformEstablishmentListing
    {
        private TaskCompletionSource? _gate;
        private Exception? _gateFailure;
        private Exception? _nextFailure;

        public string Adapter => "Avalara";

        public int Calls { get; private set; }

        public List<string> Tenants { get; } = [];

        public CancellationToken LastToken { get; private set; }

        public void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(Exception? failure = null)
        {
            _gateFailure = failure;
            _gate!.SetResult();
        }

        public void FailNext(Exception failure) => _nextFailure = failure;

        public async Task<IReadOnlyList<PlatformEstablishment>> ListAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        {
            Calls++;
            Tenants.Add(profile.TenantId);
            LastToken = ct;
            if (_gate is { } gate)
            {
                await gate.Task;
                _gate = null;
                if (_gateFailure is { } held)
                {
                    throw held;
                }
            }

            if (_nextFailure is { } failure)
            {
                _nextFailure = null;
                throw failure;
            }

            return establishments;
        }
    }

    private sealed class StubClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
