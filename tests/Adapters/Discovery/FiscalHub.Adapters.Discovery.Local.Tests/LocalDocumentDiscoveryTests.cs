using FiscalHub.Application.Inbound;

namespace FiscalHub.Adapters.Discovery.Local.Tests;

/// <summary>Especifica a descoberta pull local: filtra o catálogo por período/empresa/filial.</summary>
public class LocalDocumentDiscoveryTests
{
    private static DiscoveryCriteria Junho(string? company = null, string? branch = null) => new()
    {
        TenantId = "tenant-a",
        Start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(-3)),
        End = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.FromHours(-3)),
        Company = company,
        Establishment = branch,
    };

    [Fact]
    public async Task Returns_all_documents_of_the_period_when_no_company_filter()
    {
        var discovery = new LocalDocumentDiscovery();

        IReadOnlyList<DocumentReference> found = await discovery.DiscoverAsync(Junho());

        Assert.Equal(2, found.Count);
        Assert.All(found, r => Assert.Equal("nfe", r.Locator.Split('/')[0]));
        Assert.All(found, r => Assert.Equal(44, r.NaturalKey.Length)); // chave de acesso da NF-e
    }

    [Fact]
    public async Task Filters_by_company()
    {
        var discovery = new LocalDocumentDiscovery();

        IReadOnlyList<DocumentReference> found = await discovery.DiscoverAsync(Junho(company: "98765432"));

        DocumentReference only = Assert.Single(found);
        Assert.StartsWith("35260698765432", only.NaturalKey);
    }

    [Fact]
    public async Task Empty_when_period_has_no_documents()
    {
        var discovery = new LocalDocumentDiscovery();
        var criteria = new DiscoveryCriteria
        {
            TenantId = "tenant-a",
            Start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Empty(await discovery.DiscoverAsync(criteria));
    }

    [Fact]
    public async Task Empty_for_unknown_tenant()
    {
        var discovery = new LocalDocumentDiscovery();
        var criteria = Junho() with { TenantId = "tenant-x" };

        Assert.Empty(await discovery.DiscoverAsync(criteria));
    }
}
