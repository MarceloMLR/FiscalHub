using FiscalHub.Adapters.Outbound.Avalara;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Adapters.Outbound.Avalara.Tests;

/// <summary>Especifica o mapeamento domínio → Avalara (escrito antes da implementação — TDD).</summary>
public class GoodsInvoiceToAvalaraTests
{
    private const string AccessKey = "35260612345678000190550010000001231000000123";

    [Fact]
    public void Maps_header_and_partner()
    {
        AvalaraDocument doc = GoodsInvoiceToAvalara.Map(SampleInvoice());

        Assert.Equal(AccessKey, doc.ChaveNFe);
        Assert.Equal(55, doc.Modelo);
        Assert.Equal("123", doc.NumeroDocumento);
        // o "parceiro" do documento é o destinatário (a outra ponta da operação)
        Assert.Equal("98765432000110", doc.Parceiro!.Cnpj);
    }

    [Fact]
    public void Maps_reform_taxes_into_generic_impostos_array()
    {
        AvalaraDocument doc = GoodsInvoiceToAvalara.Map(SampleInvoice());

        AvalaraItem item = Assert.Single(doc.Itens);
        Assert.Equal(5102, item.Cfop);

        // o IBS/CBS explícito do domínio vira N entradas no array genérico da Avalara
        AvalaraImposto ibs = item.Impostos.Single(i => i.Imposto!.Codigo == "IBS");
        Assert.Equal("000", ibs.SituacaoTributariaImposto!.Codigo);
        Assert.Equal("000001", ibs.ClassificacaoTributariaImposto!.Codigo);
        Assert.Equal(10.50m, ibs.ValorTributoBruto);

        AvalaraImposto cbs = item.Impostos.Single(i => i.Imposto!.Codigo == "CBS");
        Assert.Equal(0.90m, cbs.ValorTributoBruto);
    }

    private static GoodsInvoice SampleInvoice() => new()
    {
        AccessKey = AccessKey,
        Model = "55",
        Series = "1",
        Number = "123",
        IssueDate = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
        Issuer = new Party { TaxId = "12345678000190", Name = "Emitente LTDA" },
        Recipient = new Party { TaxId = "98765432000110", Name = "Cliente SA" },
        TotalAmount = 100.00m,
        Items =
        [
            new GoodsInvoiceItem
            {
                Number = 1,
                ProductCode = "PROD-001",
                Description = "Produto de Teste",
                Ncm = "12345678",
                Cfop = "5102",
                Quantity = 2m,
                UnitAmount = 50m,
                TotalAmount = 100m,
                ReformTaxes = new ReformTaxes
                {
                    Cst = "000",
                    ClassTrib = "000001",
                    TaxBase = 100m,
                    IbsCbs = new IbsCbs
                    {
                        IbsState = new TaxShare { Rate = 8.50m, Amount = 8.50m },
                        IbsMunicipality = new TaxShare { Rate = 2.00m, Amount = 2.00m },
                        IbsTotalAmount = 10.50m,
                        Cbs = new TaxShare { Rate = 0.90m, Amount = 0.90m },
                    },
                },
            },
        ],
    };
}
