using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Inbound.Xml.Tests;

/// <summary>
/// Especifica o locator do XML (ADR-0028): ele aponta para o espaço de entrada do próprio tenant da referência,
/// <c>nfe/{tenant}/{arquivo}</c>. O armazenamento de fotos (<c>traces</c>) nunca é origem, nem no próprio tenant —
/// é regra própria, que vale mesmo que a do prefixo seja afrouxada.
/// </summary>
public class XmlLocatorTests
{
    [Theory]
    [InlineData("tenant-a")]
    [InlineData("tenant-b")]
    public void Trace_storage_is_never_a_source_not_even_for_its_own_tenant(string tenant)
    {
        string? problem = Check(tenant, "traces/tenant-a/202609/nfe-1/source.xml");

        Assert.NotNull(problem);
        Assert.Contains("traces", problem);
        Assert.Contains("nunca é origem", problem);
    }

    [Fact]
    public void Trace_rule_holds_even_when_the_prefix_rule_would_accept()
    {
        // Uma instalação que usasse "traces" como container de entrada ainda assim não ingeriria fotos.
        string? problem = XmlLocator.Check(Reference("tenant-a", "traces/tenant-a/nfe-1.xml"), inboxContainer: "traces");

        Assert.NotNull(problem);
        Assert.Contains("nunca é origem", problem);
    }

    [Fact]
    public void Another_tenants_space_is_refused()
    {
        string? problem = Check("tenant-b", "nfe/tenant-a/nfe-1.xml");

        Assert.NotNull(problem);
        Assert.Contains("nfe/tenant-b/", problem);
    }

    [Fact]
    public void Another_container_is_refused()
    {
        string? problem = Check("tenant-b", "drop/tenant-b/nfe-1.xml");

        Assert.NotNull(problem);
        Assert.Contains("'nfe'", problem);
    }

    [Theory]
    [InlineData("nfe/tenant-b/../tenant-a/nfe-1.xml")]
    [InlineData("nfe/tenant-b/./nfe-1.xml")]
    [InlineData("nfe\\tenant-b\\nfe-1.xml")]
    [InlineData("nfe/tenant-b//nfe-1.xml")]
    public void Path_traversal_and_malformed_paths_are_refused(string locator)
    {
        Assert.NotNull(Check("tenant-b", locator));
    }

    [Theory]
    [InlineData("nfe/tenant-b/")]
    [InlineData("nfe/tenant-b")]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_file_is_refused(string locator)
    {
        Assert.NotNull(Check("tenant-b", locator));
    }

    [Theory]
    [InlineData("nfe/tenant-b/nfe-1.xml")]
    [InlineData("nfe/tenant-b/lote-7/nfe-1.xml")]
    public void Own_space_is_accepted(string locator)
    {
        Assert.Null(Check("tenant-b", locator));
    }

    private static string? Check(string tenant, string locator)
        => XmlLocator.Check(Reference(tenant, locator), XmlLocator.DefaultInboxContainer);

    private static DocumentReference Reference(string tenant, string locator) => new()
    {
        TenantId = tenant,
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "nfe-1",
        Locator = locator,
        Origin = "Xml",
    };
}
