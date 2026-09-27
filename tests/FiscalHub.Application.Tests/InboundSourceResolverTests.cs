using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Tests;

/// <summary>
/// A referência diz de onde o documento veio; o perfil do tenant só responde quando ela não diz
/// (mensagens antigas). Origem sem adapter falha nomeando a origem e o tenant.
/// </summary>
public class InboundSourceResolverTests
{
    private sealed record TestDocument(string Id);

    private static readonly FakeSource Xml = new("Xml");
    private static readonly FakeSource D365 = new("Dynamics365");

    [Fact]
    public async Task Origin_on_the_reference_wins_over_the_profile()
    {
        var resolver = Resolver(new FakeProfiles(("tenant-a", "Dynamics365")));

        IInboundSource<TestDocument> source = await resolver.ResolveAsync(Reference("tenant-a", origin: "Xml"));

        Assert.Same(Xml, source);
    }

    [Fact]
    public async Task Without_origin_the_profile_inbound_adapter_is_used()
    {
        var resolver = Resolver(new FakeProfiles(("tenant-a", "Dynamics365")));

        IInboundSource<TestDocument> source = await resolver.ResolveAsync(Reference("tenant-a", origin: null));

        Assert.Same(D365, source);
    }

    [Fact]
    public async Task Origin_without_registered_adapter_fails_naming_origin_and_tenant()
    {
        var resolver = Resolver(new FakeProfiles(("tenant-b", "iScala")));

        var ex = await Assert.ThrowsAsync<InboundSourceNotFoundException>(
            () => resolver.ResolveAsync(Reference("tenant-b", origin: null)));

        Assert.Contains("iScala", ex.Message);
        Assert.Contains("tenant-b", ex.Message);
    }

    [Fact]
    public async Task Without_origin_and_without_profile_fails_naming_the_tenant()
    {
        var resolver = Resolver(new FakeProfiles());

        var ex = await Assert.ThrowsAsync<InboundSourceNotFoundException>(
            () => resolver.ResolveAsync(Reference("tenant-z", origin: null)));

        Assert.Contains("tenant-z", ex.Message);
        Assert.Contains("perfil", ex.Message);
    }

    [Fact]
    public async Task Two_origins_for_the_same_tenant_each_go_to_its_own_source()
    {
        var resolver = Resolver(new FakeProfiles(("tenant-a", "Dynamics365")));

        IInboundSource<TestDocument> fromDrop = await resolver.ResolveAsync(Reference("tenant-a", origin: "Xml"));
        IInboundSource<TestDocument> fromFeed = await resolver.ResolveAsync(Reference("tenant-a", origin: "Dynamics365"));

        Assert.Same(Xml, fromDrop);
        Assert.Same(D365, fromFeed);
    }

    [Fact]
    public async Task Origin_match_is_exact()
    {
        var resolver = Resolver(new FakeProfiles());

        await Assert.ThrowsAsync<InboundSourceNotFoundException>(
            () => resolver.ResolveAsync(Reference("tenant-a", origin: "dynamics365")));
    }

    private static InboundSourceResolver<TestDocument> Resolver(FakeProfiles profiles)
        => new([Xml, D365], profiles);

    private static DocumentReference Reference(string tenant, string? origin) => new()
    {
        TenantId = tenant,
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "key-1",
        Locator = "loc-1",
        Origin = origin,
    };

    private sealed class FakeSource(string origin) : IInboundSource<TestDocument>
    {
        public string Origin => origin;

        public Task<FetchResult<TestDocument>> FetchAsync(DocumentReference reference, CancellationToken ct = default)
            => throw new NotSupportedException("O resolver não busca.");
    }

    private sealed class FakeProfiles(params (string Tenant, string Adapter)[] profiles) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(profiles.Where(p => p.Tenant == tenantId).Select(p => (TenantConnectorProfile?)new TenantConnectorProfile
            {
                TenantId = p.Tenant,
                Environment = "Sandbox",
                Realtime = true,
                InboundAdapter = p.Adapter,
                OutboundAdapter = "Avalara",
            }).FirstOrDefault());

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
