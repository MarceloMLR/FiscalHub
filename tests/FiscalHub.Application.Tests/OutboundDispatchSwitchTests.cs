using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;

namespace FiscalHub.Application.Tests;

/// <summary>
/// A leitura da chave de envio do tenant (ADR-0041): o padrão é enviar — inclusive quando não há perfil nenhum —, a
/// chave gravada desligada desliga, e o estado de um tenant não atravessa para outro.
/// </summary>
public class OutboundDispatchSwitchTests
{
    [Fact]
    public async Task A_profile_that_never_touched_the_switch_is_on()
    {
        var switchOf = new OutboundDispatchSwitch(new FakeProfiles { ["tenant-a"] = Profile("tenant-a") });

        Assert.True(await switchOf.IsOnAsync("tenant-a"));
    }

    [Fact]
    public async Task A_profile_saved_switched_off_is_off()
    {
        var switchOf = new OutboundDispatchSwitch(new FakeProfiles
        {
            ["tenant-a"] = Profile("tenant-a") with { OutboundDispatchEnabled = false },
        });

        Assert.False(await switchOf.IsOnAsync("tenant-a"));
    }

    [Fact]
    public async Task A_tenant_without_a_profile_is_on()
    {
        // O comportamento de hoje: quem ainda não configurou nada não para de enviar por uma chave que ninguém desligou.
        var switchOf = new OutboundDispatchSwitch(new FakeProfiles());

        Assert.True(await switchOf.IsOnAsync("tenant-sem-perfil"));
    }

    [Fact]
    public async Task The_switch_of_one_tenant_does_not_reach_another()
    {
        var switchOf = new OutboundDispatchSwitch(new FakeProfiles
        {
            ["tenant-a"] = Profile("tenant-a") with { OutboundDispatchEnabled = false },
            ["tenant-c"] = Profile("tenant-c"),
        });

        Assert.False(await switchOf.IsOnAsync("tenant-a"));
        Assert.True(await switchOf.IsOnAsync("tenant-c"));
    }

    private static TenantConnectorProfile Profile(string tenantId) => new()
    {
        TenantId = tenantId,
        InboundAdapter = "Dynamics365",
    };

    private sealed class FakeProfiles : Dictionary<string, TenantConnectorProfile>, IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(TryGetValue(tenantId, out TenantConnectorProfile? profile) ? profile : null);

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
