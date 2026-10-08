using System.Net;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;
using FiscalHub.Application.Inbound;
using FiscalHub.Adapters.Ingress.D365Poll;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Integration.Tests;

/// <summary>
/// Sem credencial no perfil, a integração automática FALHA com o motivo, em vez de ler o F&amp;O com outra identidade
/// (change explicit-credential-and-execution-cnpj, D2; spec d365-change-feed, "A falha aparece no painel da integração
/// automática"). O coletor real sobre o feed do D365 como o host o registra, com o provider de token de produção. O cursor,
/// o lease e a fila são de memória; o F&amp;O é um handler que conta as requisições. O último erro do cursor é o texto que o
/// painel mostra.
/// </summary>
public class D365CredentialRequiredTests
{
    private const string Tenant = "tenant-a";
    private static readonly DateTimeOffset StartFrom = new(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string> Profiles => new()
    {
        // sem auth
        { "", "A credencial do ERP não está configurada: o perfil não tem Tenant do Entra ID, Client ID nem Client Secret." },
        // auth incompleto, como o seed de dev o grava
        {
            ""","auth":{"tenantId":"","clientId":"","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"}""",
            "A credencial do ERP está incompleta: falta Tenant do Entra ID e Client ID."
        },
        // auth completo, e o cofre sem o segredo
        {
            ""","auth":{"tenantId":"entra-a","clientId":"app-a","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"}""",
            "O Client Secret do ERP do tenant 'tenant-a' não está no cofre."
        },
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task Without_a_usable_credential_the_pass_records_the_reason_and_nothing_reaches_the_erp(string auth, string reason)
    {
        var h = new Harness(auth);

        ChangeFeedPassSummary summary = await h.RunPassAsync();

        ChangeFeedCursor cursor = h.Cursors.Get(Tenant);
        Assert.Equal(1, cursor.ConsecutiveFailures);
        Assert.StartsWith(reason, cursor.LastError);              // o último erro do painel
        Assert.Contains("Configurações → Conectores → Entrada", cursor.LastError);
        Assert.Equal(StartFrom, cursor.Watermark);               // a marca não avança
        Assert.StartsWith(reason, summary.Failures[Tenant]);
        Assert.Equal(0, h.Erp.Requests);                         // nenhuma requisição ao F&O
        Assert.Empty(h.Queue.References);
    }

    [Fact]
    public async Task The_three_reasons_reach_the_panel_distinct()
    {
        var errors = new List<string>();
        foreach (object[] row in Profiles)
        {
            var h = new Harness((string)row[0]);
            await h.RunPassAsync();
            errors.Add(h.Cursors.Get(Tenant).LastError!);
        }

        Assert.Equal(3, errors.Distinct(StringComparer.Ordinal).Count());
    }

    private sealed class Harness
    {
        private readonly ServiceProvider _services;
        private readonly TenantConnectorProfile _profile;

        public Harness(string auth)
        {
            _profile = new TenantConnectorProfile
            {
                TenantId = Tenant,
                Environment = "Sandbox",
                InboundAdapter = "Dynamics365",
                InboundSettings = $$$"""{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"]{{{auth}}},"poll":{"enabled":true,"intervalSeconds":60,"overlapSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}}""",
                OutboundAdapter = "Avalara",
            };

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConnectorProfileStore>(new OneProfile(_profile));
            services.AddSingleton<ISecretStore, EmptyVault>();
            services.AddD365ChangeFeed();   // o feed e o provider de token, como o host os registra
            services.AddHttpClient("d365-odata").ConfigurePrimaryHttpMessageHandler(() => Erp);
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public CountingErp Erp { get; } = new();

        public MemoryCursors Cursors { get; } = new();

        public MemoryQueue Queue { get; } = new();

        public async Task<ChangeFeedPassSummary> RunPassAsync()
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            var poller = new ChangeFeedPoller(
                scope.ServiceProvider.GetRequiredService<IDocumentChangeFeed>(),
                scope.ServiceProvider.GetRequiredService<IConnectorProfileStore>(),
                Cursors,
                new FreeLeases(),
                Queue,
                new ChangeFeedPublicationLog(),
                new ChangeFeedPollerOptions(),
                TimeProvider.System);
            return await poller.RunOnceAsync();
        }
    }

    private sealed class OneProfile(TenantConnectorProfile profile) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantConnectorProfile?>(tenantId == profile.TenantId ? profile : null);

        public Task UpsertAsync(TenantConnectorProfile p, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>(inboundAdapter == profile.InboundAdapter ? [profile] : []);
    }

    private sealed class EmptyVault : ISecretStore
    {
        public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task SetAsync(string name, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default) => Task.FromResult<SecretDescription?>(null);
    }

    internal sealed class CountingErp : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    internal sealed class MemoryCursors : IChangeFeedCursorStore
    {
        private readonly Dictionary<string, ChangeFeedCursor> _cursors = [];

        public ChangeFeedCursor Get(string tenantId) => _cursors[tenantId];

        public Task<ChangeFeedCursor?> GetAsync(string tenantId, string origin, CancellationToken ct = default)
            => Task.FromResult(_cursors.GetValueOrDefault(tenantId));

        public Task<ChangeFeedCursor> StartAsync(string tenantId, string origin, DateTimeOffset initialWatermark, CancellationToken ct = default)
        {
            ChangeFeedCursor cursor = _cursors.GetValueOrDefault(tenantId) is { Watermark: not null } existing
                ? existing
                : (_cursors.GetValueOrDefault(tenantId) ?? new ChangeFeedCursor { TenantId = tenantId, Origin = origin }) with { Watermark = initialWatermark };
            _cursors[tenantId] = cursor;
            return Task.FromResult(cursor);
        }

        public Task<bool> TryAdvanceWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
        {
            _cursors[tenantId] = _cursors[tenantId] with { Watermark = watermark };
            return Task.FromResult(true);
        }

        public Task<bool> TryRewindWatermarkAsync(string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordSuccessAsync(string tenantId, string origin, DateTimeOffset polledAt, CancellationToken ct = default)
        {
            _cursors[tenantId] = _cursors[tenantId] with { LastPolledAt = polledAt, ConsecutiveFailures = 0, LastError = null };
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            string tenantId, string origin, DateTimeOffset polledAt, string error, DateTimeOffset? notBefore, CancellationToken ct = default)
        {
            ChangeFeedCursor cursor = _cursors.GetValueOrDefault(tenantId) ?? new ChangeFeedCursor { TenantId = tenantId, Origin = origin };
            _cursors[tenantId] = cursor with
            {
                LastPolledAt = polledAt,
                ConsecutiveFailures = cursor.ConsecutiveFailures + 1,
                LastError = error,
                NotBefore = notBefore,
            };
            return Task.CompletedTask;
        }
    }

    private sealed class FreeLeases : ILeaseStore
    {
        public Task<bool> TryAcquireAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> RenewAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(true);

        public Task ReleaseAsync(string resource, string owner, CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class MemoryQueue : IDocumentQueue
    {
        public List<DocumentReference> References { get; } = [];

        public Task EnqueueAsync(DocumentReference reference, CancellationToken ct = default)
        {
            References.Add(reference);
            return Task.CompletedTask;
        }
    }
}
