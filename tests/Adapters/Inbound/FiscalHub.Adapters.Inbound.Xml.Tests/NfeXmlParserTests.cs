using System.Xml.Linq;
using FiscalHub.Adapters.Inbound.Xml;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Adapters.Inbound.Xml.Tests;

/// <summary>Especifica o comportamento do parser de NF-e (escrito antes da implementação — TDD).</summary>
public class NfeXmlParserTests
{
    private static string LoadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parses_valid_nfe_into_goods_invoice_with_reform_taxes()
    {
        var parser = new NfeXmlParser();
        string xml = LoadFixture("nfe-com-reforma.xml");

        GoodsInvoice invoice = parser.Parse(xml);

        // cabeçalho
        Assert.Equal("35260612345678000190550010000001231000000123", invoice.AccessKey);
        Assert.Equal("55", invoice.Model);
        Assert.Equal("12345678000190", invoice.Issuer.TaxId);
        Assert.Equal("98765432000110", invoice.Recipient.TaxId);

        // item
        var item = Assert.Single(invoice.Items);
        Assert.Equal("5102", item.Cfop);
        Assert.Equal("12345678", item.Ncm);

        // bloco da reforma (Grupo UB)
        var reform = item.ReformTaxes!;   // esta fixture traz o grupo; sem ele, o item sai com o grupo ausente
        Assert.Equal("000", reform.Cst);
        Assert.Equal("000001", reform.ClassTrib);
        Assert.Equal(100.00m, reform.TaxBase);
        Assert.Equal(8.50m, reform.IbsCbs.IbsState.Amount);
        Assert.Equal(2.00m, reform.IbsCbs.IbsMunicipality.Amount);
        Assert.Equal(10.50m, reform.IbsCbs.IbsTotalAmount);
        Assert.Equal(0.90m, reform.IbsCbs.Cbs.Amount);
    }

    [Fact]
    public void Missing_required_field_throws_parse_exception()
    {
        var parser = new NfeXmlParser();
        string xml = LoadFixture("nfe-sem-cfop.xml");

        Assert.Throws<NfeParseException>(() => parser.Parse(xml));
    }

    [Fact]
    public void Nfe_without_the_reform_group_is_read_with_the_group_absent()
    {
        // Nota anterior à Reforma: o grupo não é julgado na leitura; fica ausente, e não zerado (ADR-0026).
        string xml = WithoutElement(LoadFixture("nfe-com-reforma.xml"), "IBSCBS");

        GoodsInvoice invoice = new NfeXmlParser().Parse(xml);

        GoodsInvoiceItem item = Assert.Single(invoice.Items);
        Assert.Null(item.ReformTaxes);
        Assert.Equal("5102", item.Cfop);
    }

    [Fact]
    public void Reform_group_without_its_inner_group_is_still_a_read_failure()
    {
        // IBSCBS presente e incompleto é estrutura quebrada do documento de origem, não conteúdo fiscal.
        string xml = WithoutElement(LoadFixture("nfe-com-reforma.xml"), "gIBSCBS");

        NfeParseException ex = Assert.Throws<NfeParseException>(() => new NfeXmlParser().Parse(xml));
        Assert.Contains("gIBSCBS", ex.Message);
    }

    private static string WithoutElement(string xml, string localName)
    {
        XDocument doc = XDocument.Parse(xml);
        foreach (XElement el in doc.Descendants().Where(e => e.Name.LocalName == localName).ToList())
        {
            el.Remove();
        }

        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
