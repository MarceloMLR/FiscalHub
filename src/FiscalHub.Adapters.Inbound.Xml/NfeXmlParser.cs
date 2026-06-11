using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>Converte o XML de uma NF-e (modelo 55) na <see cref="GoodsInvoice"/> do domínio.</summary>
public sealed class NfeXmlParser
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>Lê o XML e devolve a nota no modelo de domínio. Lança <see cref="NfeParseException"/> se inválido.</summary>
    public GoodsInvoice Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new NfeParseException($"XML inválido: {ex.Message}");
        }

        XElement infNFe = doc.Descendants(Nfe + "infNFe").FirstOrDefault()
            ?? throw new NfeParseException("Elemento infNFe não encontrado.");

        string rawId = infNFe.Attribute("Id")?.Value
            ?? throw new NfeParseException("Atributo Id da infNFe ausente.");
        string accessKey = rawId.StartsWith("NFe", StringComparison.Ordinal) ? rawId[3..] : rawId;

        XElement ide = Required(infNFe, "ide");
        XElement total = Required(Required(infNFe, "total"), "ICMSTot");

        List<GoodsInvoiceItem> items = infNFe.Elements(Nfe + "det").Select(ParseItem).ToList();
        if (items.Count == 0)
            throw new NfeParseException("A nota não possui itens (det).");

        return new GoodsInvoice
        {
            AccessKey = accessKey,
            Model = Value(ide, "mod"),
            Series = Value(ide, "serie"),
            Number = Value(ide, "nNF"),
            IssueDate = DateTimeOffset.Parse(Value(ide, "dhEmi"), CultureInfo.InvariantCulture),
            IbsCbsTaxableMunicipality = Optional(ide, "cMunFGIBS"),
            Issuer = ParseParty(Required(infNFe, "emit")),
            Recipient = ParseParty(Required(infNFe, "dest")),
            Items = items,
            TotalAmount = Decimal(total, "vNF"),
        };
    }

    private static GoodsInvoiceItem ParseItem(XElement det)
    {
        XElement prod = Required(det, "prod");
        int number = int.Parse(
            det.Attribute("nItem")?.Value ?? throw new NfeParseException("Item sem atributo nItem."),
            CultureInfo.InvariantCulture);

        return new GoodsInvoiceItem
        {
            Number = number,
            ProductCode = Value(prod, "cProd"),
            Description = Value(prod, "xProd"),
            Ncm = Value(prod, "NCM"),
            Cfop = Value(prod, "CFOP"),
            Quantity = Decimal(prod, "qCom"),
            UnitAmount = Decimal(prod, "vUnCom"),
            TotalAmount = Decimal(prod, "vProd"),
            ReformTaxes = ParseReformTaxes(Required(det, "imposto")),
        };
    }

    private static ReformTaxes ParseReformTaxes(XElement imposto)
    {
        XElement ibsCbs = Required(imposto, "IBSCBS");
        XElement g = Required(ibsCbs, "gIBSCBS");
        XElement uf = Required(g, "gIBSUF");
        XElement mun = Required(g, "gIBSMun");
        XElement cbs = Required(g, "gCBS");

        return new ReformTaxes
        {
            Cst = Value(ibsCbs, "CST"),
            ClassTrib = Value(ibsCbs, "cClassTrib"),
            TaxBase = Decimal(g, "vBC"),
            IbsCbs = new IbsCbs
            {
                IbsState = new TaxShare { Rate = Decimal(uf, "pIBSUF"), Amount = Decimal(uf, "vIBSUF") },
                IbsMunicipality = new TaxShare { Rate = Decimal(mun, "pIBSMun"), Amount = Decimal(mun, "vIBSMun") },
                IbsTotalAmount = Decimal(g, "vIBS"),
                Cbs = new TaxShare { Rate = Decimal(cbs, "pCBS"), Amount = Decimal(cbs, "vCBS") },
            },
            SelectiveTax = ParseSelectiveTax(imposto),
        };
    }

    private static SelectiveTax? ParseSelectiveTax(XElement imposto)
    {
        XElement? isEl = imposto.Element(Nfe + "IS");
        if (isEl is null)
            return null;

        return new SelectiveTax
        {
            Cst = Value(isEl, "CST"),
            ClassTrib = Value(isEl, "cClassTrib"),
            TaxBase = Decimal(isEl, "vBC"),
            Rate = Decimal(isEl, "pIS"),
            TaxableUnit = Optional(isEl, "uTrib"),
            TaxableQuantity = OptionalDecimal(isEl, "qTrib"),
            Amount = Decimal(isEl, "vIS"),
        };
    }

    private static Party ParseParty(XElement el) => new()
    {
        TaxId = Optional(el, "CNPJ") ?? Optional(el, "CPF")
            ?? throw new NfeParseException("Parte sem CNPJ ou CPF."),
        Name = Value(el, "xNome"),
        StateRegistration = Optional(el, "IE"),
        MunicipalityCode = Optional(el, "cMun"),
    };

    // ---- helpers ----

    private static XElement Required(XElement parent, string name)
        => parent.Element(Nfe + name)
           ?? throw new NfeParseException($"Elemento obrigatório ausente: {name}");

    private static string Value(XElement parent, string name) => Required(parent, name).Value;

    private static string? Optional(XElement parent, string name) => parent.Element(Nfe + name)?.Value;

    private static decimal Decimal(XElement parent, string name)
        => decimal.Parse(Value(parent, name), CultureInfo.InvariantCulture);

    private static decimal? OptionalDecimal(XElement parent, string name)
    {
        string? raw = Optional(parent, name);
        return raw is null ? null : decimal.Parse(raw, CultureInfo.InvariantCulture);
    }
}
