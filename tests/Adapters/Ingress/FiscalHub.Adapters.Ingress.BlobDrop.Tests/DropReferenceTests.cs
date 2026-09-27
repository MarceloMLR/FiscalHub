using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Ingress.BlobDrop.Tests;

/// <summary>
/// A referência de um drop diz que a origem é XML (ADR-0025): o tenant-a tem o D365 no perfil e ainda
/// assim o XML dele precisa ir para o adapter de XML.
/// </summary>
public class DropReferenceTests
{
    [Fact]
    public void Drop_reference_carries_the_xml_origin()
    {
        DocumentReference reference = DropReference.For("tenant-a", "nfe-600", "inbox/tenant-a/nfe-600.xml");

        Assert.Equal("tenant-a", reference.TenantId);
        Assert.Equal("nfe-600", reference.NaturalKey);
        Assert.Equal("inbox/tenant-a/nfe-600.xml", reference.Locator);
        Assert.Equal(DocumentType.GoodsInvoice55, reference.Type);
        Assert.Equal("Xml", reference.Origin);
        Assert.Equal(IngestionTrigger.Event, reference.Trigger);
    }
}
