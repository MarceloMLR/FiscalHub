using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>
/// Especifica o registro do adapter (ADR-0027): autenticação real por padrão, "sem autenticação" só por pedido explícito
/// e com aviso, nenhum valor de cabeçalho no log dos clientes HTTP, e salvar o perfil chegando ao <c>Forget</c>.
/// </summary>
public class AvalaraRegistrationTests
{
    [Fact]
    public async Task Default_composition_authenticates_with_the_real_provider()
    {
        await using ServiceProvider sp = Build(services => services.AddAvalaraComplianceDispatcher());

        Assert.IsType<AvalaraTokenProvider>(sp.GetRequiredService<IAvalaraTokenProvider>());
        Assert.Same(sp.GetRequiredService<IAvalaraTokenProvider>(), sp.GetRequiredService<IAvalaraTokenProvider>());   // um cache por processo
        await using AsyncServiceScope scope = sp.CreateAsyncScope();
        Assert.IsType<AvalaraComplianceDispatcher>(scope.ServiceProvider.GetRequiredService<IComplianceDispatcher<GoodsInvoice>>());
    }

    [Fact]
    public async Task Without_authentication_only_when_asked_and_with_a_warning()
    {
        var logs = new CapturingLoggerProvider();
        await using ServiceProvider sp = Build(
            services => services.AddAvalaraComplianceDispatcher().UseAvalaraWithoutAuthentication(), logs);

        Assert.IsType<NoOpAvalaraTokenProvider>(sp.GetRequiredService<IAvalaraTokenProvider>());
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("SEM autenticação"));
    }

    [Fact]
    public async Task Both_http_clients_redact_every_header_value_in_their_logs()
    {
        await using ServiceProvider sp = Build(services => services.AddAvalaraComplianceDispatcher());
        IOptionsMonitor<HttpClientFactoryOptions> options = sp.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();

        foreach (string client in new[] { "avalara-token", "IComplianceDispatcher<GoodsInvoice>" })
        {
            Func<string, bool> redact = options.Get(client).ShouldRedactHeaderValue;
            Assert.True(redact("Authorization"), client);
            Assert.True(redact("X-Qualquer"), client);
            // O padrão do framework também redige tudo; a regra é nossa, explícita, e não depende dele.
            Assert.Equal(typeof(AvalaraOptions).Assembly, redact.Method.DeclaringType!.Assembly);
        }
    }

    [Fact]
    public async Task Saving_the_profile_through_the_service_reaches_forget()
    {
        var tokens = new RecordingTokenProvider();
        await using ServiceProvider sp = Build(services =>
        {
            services.AddAvalaraComplianceDispatcher();
            services.Replace(ServiceDescriptor.Singleton<IAvalaraTokenProvider>(tokens));
            services.AddSingleton<ITenantContext>(new Tenant("tenant-a"));
            services.AddScoped<ConnectorProfileService>();
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        ConnectorProfileSaveResult result = await scope.ServiceProvider.GetRequiredService<ConnectorProfileService>().SaveAsync(
            new ConnectorProfileRequest("Sandbox", "Xml", "{}", "Avalara", """{"sandbox":{"clientId":"id-a","clientSecret":"novo"}}"""));

        Assert.Equal(ConnectorProfileSaveStatus.Saved, result.Status);
        Assert.Equal(["tenant-a"], tokens.Forgotten);
    }

    private static ServiceProvider Build(Action<IServiceCollection> register, CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logs is not null)
            {
                b.AddProvider(logs);
            }
        });
        services.AddSingleton<ISecretStore, InMemorySecrets>();
        services.AddSingleton<IConnectorProfileStore, InMemoryProfiles>();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class RecordingTokenProvider : IAvalaraTokenProvider
    {
        public List<string> Forgotten { get; } = [];

        public Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void Invalidate(AvalaraAccessToken token)
        {
        }

        public void Forget(string tenantId) => Forgotten.Add(tenantId);

        public Task<CredentialTestOutcome> ProbeAsync(AvalaraOutboundSettings settings, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class InMemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult(_values.GetValueOrDefault(name));

        public Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            _values[name] = value;
            return Task.CompletedTask;
        }

        public Task<SecretDescription?> DescribeAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_values.ContainsKey(name) ? new SecretDescription(DateTimeOffset.UnixEpoch) : null);
    }

    private sealed class InMemoryProfiles : IConnectorProfileStore
    {
        private readonly Dictionary<string, TenantConnectorProfile> _profiles = [];

        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_profiles.GetValueOrDefault(tenantId));

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default)
        {
            _profiles[profile.TenantId] = profile;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([.. _profiles.Values.Where(p => p.InboundAdapter == inboundAdapter)]);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly CapturingLogger<object> _logger = new();

        public IReadOnlyList<(LogLevel Level, string Text)> Entries => _logger.Entries;

        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
        }
    }
}
