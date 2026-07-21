using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o cache de token: uma única busca por tenant mesmo sob concorrência, reuso enquanto
/// válido, renovação dentro da margem de segurança e isolamento entre tenants.
/// </summary>
public class AvalaraTokenProviderTests
{
    [Fact]
    public async Task Concurrent_calls_fetch_the_token_only_once()
    {
        // 20 esteiras pedindo token ao mesmo tempo, cache vazio. O atraso garante que as chamadas
        // realmente se sobreponham (senão a primeira terminaria antes das outras começarem).
        var endpoint = new TokenEndpointStub { Delay = TimeSpan.FromMilliseconds(80) };
        AvalaraTokenProvider provider = Build(endpoint);

        string[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => provider.GetTokenAsync("tenant-a")));

        Assert.Equal(1, endpoint.Calls);                      // buscou UMA vez só
        Assert.All(tokens, t => Assert.Equal(tokens[0], t));  // todas receberam o mesmo token
    }

    [Fact]
    public async Task Valid_cached_token_is_reused()
    {
        var endpoint = new TokenEndpointStub();
        AvalaraTokenProvider provider = Build(endpoint);

        string first = await provider.GetTokenAsync("tenant-a");
        string second = await provider.GetTokenAsync("tenant-a");

        Assert.Equal(1, endpoint.Calls);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Token_is_renewed_only_inside_the_safety_margin()
    {
        // Token expira em 1h; margem padrão de 5 min.
        var endpoint = new TokenEndpointStub { ExpiresInSeconds = 3600 };
        var clock = new FakeClock();
        AvalaraTokenProvider provider = Build(endpoint, clock);

        await provider.GetTokenAsync("tenant-a");

        clock.Advance(TimeSpan.FromMinutes(50));   // faltam 10 min: fora da margem, ainda vale
        await provider.GetTokenAsync("tenant-a");
        Assert.Equal(1, endpoint.Calls);

        clock.Advance(TimeSpan.FromMinutes(8));    // faltam 2 min: dentro da margem, renova
        await provider.GetTokenAsync("tenant-a");
        Assert.Equal(2, endpoint.Calls);
    }

    [Fact]
    public async Task Different_tenants_have_separate_tokens()
    {
        var endpoint = new TokenEndpointStub();
        AvalaraTokenProvider provider = Build(endpoint);

        string a = await provider.GetTokenAsync("tenant-a");
        string b = await provider.GetTokenAsync("tenant-b");

        Assert.Equal(2, endpoint.Calls);
        Assert.NotEqual(a, b);
    }

    private static AvalaraTokenProvider Build(TokenEndpointStub endpoint, TimeProvider? clock = null)
    {
        var http = new HttpClient(endpoint) { BaseAddress = new Uri("http://localhost/") };
        var options = Options.Create(new AvalaraOptions
        {
            BaseUrl = "http://localhost/",
            ClientId = "id-de-teste",
            ClientSecret = "segredo-de-teste",
        });

        return new AvalaraTokenProvider(http, options, clock ?? TimeProvider.System);
    }

    /// <summary>Relógio controlável — fake manual, sem libs de mock.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>Endpoint de token falso: conta as chamadas e devolve um token distinto a cada uma.</summary>
    private sealed class TokenEndpointStub : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public int ExpiresInSeconds { get; init; } = 86400;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int n = Interlocked.Increment(ref _calls);

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct);
            }

            string json = $$"""{"access_token":"tok-{{n}}","expires_in":{{ExpiresInSeconds}}}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
