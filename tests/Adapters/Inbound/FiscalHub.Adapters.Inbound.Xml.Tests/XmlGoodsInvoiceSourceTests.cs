using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Inbound.Xml.Tests;

/// <summary>
/// Especifica o source de XML: lê o Blob pelo Locator e delega ao parser. O Blob é falso
/// (IBlobReader stub) — sem Azure/Azurite no unit test.
/// </summary>
public class XmlGoodsInvoiceSourceTests
{
    [Fact]
    public async Task Fetch_reads_blob_by_locator_and_parses()
    {
        var reader = new FakeBlobReader(LoadFixture("nfe-com-reforma.xml"));
        var source = new XmlGoodsInvoiceSource(reader, new NfeXmlParser());

        GoodsInvoice invoice = await source.FetchAsync(Reference("nfe/nfe-1.xml"));

        Assert.Equal("35260612345678000190550010000001231000000123", invoice.AccessKey);
        Assert.Single(invoice.Items);
        Assert.Equal("nfe/nfe-1.xml", reader.LastLocator);   // usou o Locator da referência
    }

    [Fact]
    public void Reports_origin_xml()
    {
        var source = new XmlGoodsInvoiceSource(new FakeBlobReader(string.Empty), new NfeXmlParser());

        Assert.Equal("Xml", source.Origin);
    }

    private static string LoadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static DocumentReference Reference(string locator) => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = "nfe-1",
        Locator = locator,
    };

    private sealed class FakeBlobReader(string content) : IBlobReader
    {
        public string? LastLocator { get; private set; }

        public Task<string> ReadTextAsync(string locator, CancellationToken ct = default)
        {
            LastLocator = locator;
            return Task.FromResult(content);
        }
    }
}
