using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FiscalHub.Application.Connectors;
using FiscalHub.Infrastructure.Secrets;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o adapter do cofre (ADR-0027): o mesmo em dev (emulador) e em produção (Key Vault). O cliente do SDK é
/// escrito à mão, sem biblioteca de mock: o <see cref="SecretClient"/> tem construtor protegido e métodos virtuais
/// para isso.
/// </summary>
public class KeyVaultSecretStoreTests
{
    private const string Name = "fh-tenant-a--outbound--sandbox--clientsecret";

    [Fact]
    public async Task Set_writes_a_version_and_get_reads_it_back()
    {
        var client = new FakeSecretClient();
        var store = new KeyVaultSecretStore(client, new ManualClock());

        await store.SetAsync(Name, "s3cr3t");

        Assert.Equal("s3cr3t", await store.GetAsync(Name));
        Assert.Equal(1, client.Sets);
    }

    [Fact]
    public async Task Missing_or_empty_secret_is_null()
    {
        var client = new FakeSecretClient();
        var store = new KeyVaultSecretStore(client, new ManualClock());

        Assert.Null(await store.GetAsync(Name));

        client.Values[Name] = "";
        Assert.Null(await store.GetAsync(Name));
    }

    [Fact]
    public async Task Describe_uses_only_the_metadata_and_never_reads_the_value()
    {
        var client = new FakeSecretClient();
        var store = new KeyVaultSecretStore(client, new ManualClock());
        await store.SetAsync(Name, "s3cr3t");
        int readsBefore = client.Gets;

        SecretDescription? description = await store.DescribeAsync(Name);

        Assert.NotNull(description);
        Assert.Equal(client.UpdatedOn, description.UpdatedOn);
        Assert.Equal(readsBefore, client.Gets);   // o valor nunca foi lido
        Assert.Null(await store.DescribeAsync("fh-tenant-a--outbound--production--clientsecret"));
    }

    [Fact]
    public async Task Value_is_cached_for_the_interval_and_set_invalidates_it()
    {
        var client = new FakeSecretClient();
        var clock = new ManualClock();
        var store = new KeyVaultSecretStore(client, clock, TimeSpan.FromMinutes(5));
        client.Values[Name] = "v1";

        await store.GetAsync(Name);
        await store.GetAsync(Name);
        Assert.Equal(1, client.Gets);              // a segunda leitura veio do cache

        client.Values[Name] = "rodado-direto-no-cofre";
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("rodado-direto-no-cofre", await store.GetAsync(Name));   // o intervalo venceu
        Assert.Equal(2, client.Gets);

        await store.SetAsync(Name, "v3");          // a gravação pela tela invalida na hora
        Assert.Equal("v3", await store.GetAsync(Name));
        Assert.Equal(3, client.Gets);
    }

    [Fact]
    public async Task Vault_failure_becomes_an_exception_without_the_value()
    {
        var client = new FakeSecretClient { FailWith = 500 };
        var store = new KeyVaultSecretStore(client, new ManualClock());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync(Name, "s3cr3t"));

        Assert.Contains(Name, failure.Message);
        Assert.Contains("500", failure.Message);
        Assert.DoesNotContain("s3cr3t", failure.ToString());   // nem na mensagem, nem na interna
    }

    [Theory]
    [InlineData("jwt-signing-key")]
    [InlineData("avalara-a-sandbox-secret")]
    [InlineData("fh-com/barra")]
    public async Task Name_outside_the_connector_prefix_is_refused_before_calling_the_vault(string name)
    {
        var client = new FakeSecretClient();
        var store = new KeyVaultSecretStore(client, new ManualClock());

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(name));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(name, "x"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DescribeAsync(name));

        Assert.Equal(0, client.Calls);
    }

    /// <summary>Um <see cref="SecretClient"/> em memória: uma versão por segredo, com a data da gravação.</summary>
    private sealed class FakeSecretClient : SecretClient
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset UpdatedOn { get; } = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        public int? FailWith { get; init; }

        public int Gets { get; private set; }

        public int Sets { get; private set; }

        public int Calls { get; private set; }

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Gets++;
            Fail();
            return Values.TryGetValue(name, out string? value)
                ? Task.FromResult(Response.FromValue(new KeyVaultSecret(name, value), new FakeResponse(200)))
                : throw new RequestFailedException(404, "SecretNotFound");
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            Calls++;
            Sets++;
            Fail();
            Values[name] = value;
            return Task.FromResult(Response.FromValue(new KeyVaultSecret(name, value), new FakeResponse(200)));
        }

        public override AsyncPageable<SecretProperties> GetPropertiesOfSecretVersionsAsync(string name, CancellationToken cancellationToken = default)
        {
            Calls++;
            Fail();
            if (!Values.ContainsKey(name))
            {
                throw new RequestFailedException(404, "SecretNotFound");
            }

            SecretProperties properties = SecretModelFactory.SecretProperties(name: name, updatedOn: UpdatedOn);
            properties.Enabled = true;
            return AsyncPageable<SecretProperties>.FromPages([Page<SecretProperties>.FromValues([properties], null, new FakeResponse(200))]);
        }

        private void Fail()
        {
            if (FailWith is { } status)
            {
                throw new RequestFailedException(status, "falha simulada do cofre");
            }
        }
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];
            return false;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
