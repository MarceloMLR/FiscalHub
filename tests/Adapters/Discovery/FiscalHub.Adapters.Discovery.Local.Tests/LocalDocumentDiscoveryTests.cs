using System.Text.Json;
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
        Assert.All(found, r => Assert.StartsWith($"nfe/{r.TenantId}/", r.Locator));   // espaço de entrada do tenant (ADR-0028)
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

    [Theory]
    [InlineData("12345678", "35260612345678")]
    [InlineData("98765432", "35260698765432")]
    public async Task The_company_of_the_example_directory_is_the_one_the_catalog_accepts(string company, string keyPrefix)
    {
        // O par de desenvolvimento (change company-root-in-directory, D5): o companies.json do Host lista a raiz do CNPJ,
        // de 8 caracteres, e é essa a empresa que o catálogo local guarda e procura. Com a filial 0001, cada uma traz a nota
        // de exemplo dela.
        using JsonDocument directory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "companies.json")));
        JsonElement listed = directory.RootElement.EnumerateArray().Single(c => c.GetProperty("code").GetString() == company);
        Assert.Equal(8, company.Length);
        Assert.Contains(listed.GetProperty("branches").EnumerateArray(), b => b.GetProperty("code").GetString() == "0001");

        IReadOnlyList<DocumentReference> found = await new LocalDocumentDiscovery().DiscoverAsync(Junho(company, "0001"));

        Assert.StartsWith(keyPrefix, Assert.Single(found).NaturalKey);
    }

    [Fact]
    public async Task Every_company_of_the_example_directory_is_a_root()
    {
        using JsonDocument directory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "companies.json")));

        Assert.All(directory.RootElement.EnumerateArray(), c => Assert.Equal(8, c.GetProperty("code").GetString()!.Length));
    }

    [Fact]
    public async Task Filters_by_document_number()
    {
        var discovery = new LocalDocumentDiscovery();

        IReadOnlyList<DocumentReference> found = await discovery.DiscoverAsync(Junho() with { DocumentNumber = "456" });

        DocumentReference only = Assert.Single(found);   // só a nota nNF 456
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
    public async Task Discovered_references_carry_the_xml_origin()
    {
        var discovery = new LocalDocumentDiscovery();

        IReadOnlyList<DocumentReference> found = await discovery.DiscoverAsync(Junho());

        // A origem é a do documento (XML no Blob), não a da descoberta ("Local") — ADR-0025.
        Assert.All(found, r => Assert.Equal("Xml", r.Origin));
    }

    [Fact]
    public async Task Reference_found_by_key_carries_the_xml_origin()
    {
        var discovery = new LocalDocumentDiscovery();

        DocumentReference? reference = await discovery.FindByKeyAsync("tenant-a", "35260612345678000190550010000001231000000123");

        Assert.NotNull(reference);
        Assert.Equal("Xml", reference.Origin);
    }

    [Fact]
    public async Task Empty_for_unknown_tenant()
    {
        var discovery = new LocalDocumentDiscovery();
        var criteria = Junho() with { TenantId = "tenant-x" };

        Assert.Empty(await discovery.DiscoverAsync(criteria));
    }
}
