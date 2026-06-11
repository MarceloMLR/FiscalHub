using System.Globalization;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Adapters.Outbound.Avalara;

/// <summary>Mapeia a <see cref="GoodsInvoice"/> do domínio para o documento esperado pela Avalara.</summary>
internal static class GoodsInvoiceToAvalara
{
    public static AvalaraDocument Map(GoodsInvoice invoice) => new()
    {
        ChaveNFe = invoice.AccessKey,
        Modelo = 55,
        NumeroDocumento = invoice.Number,
        Parceiro = new AvalaraParceiro
        {
            Nome = invoice.Recipient.Name,
            Cnpj = invoice.Recipient.TaxId,
            // Codigo (código interno) é preenchido pelo enriquecimento — fatia futura.
        },
        Itens = invoice.Items.Select(MapItem).ToList(),
    };

    private static AvalaraItem MapItem(GoodsInvoiceItem item) => new()
    {
        NumeroSequencia = item.Number,
        Cfop = int.Parse(item.Cfop, CultureInfo.InvariantCulture),
        ValorTotal = item.TotalAmount,
        Impostos = MapReformTaxes(item.ReformTaxes),
    };

    // O IBS/CBS/IS, explícito no domínio, vira N entradas no array genérico da Avalara.
    private static List<AvalaraImposto> MapReformTaxes(ReformTaxes r)
    {
        var impostos = new List<AvalaraImposto>
        {
            new()
            {
                Imposto = new AvalaraCodigo { Codigo = "IBS" },
                SituacaoTributariaImposto = new AvalaraCodigo { Codigo = r.Cst },
                ClassificacaoTributariaImposto = new AvalaraCodigo { Codigo = r.ClassTrib },
                ValorBaseTributo = r.TaxBase,
                ValorTributoBruto = r.IbsCbs.IbsTotalAmount,
            },
            new()
            {
                Imposto = new AvalaraCodigo { Codigo = "CBS" },
                SituacaoTributariaImposto = new AvalaraCodigo { Codigo = r.Cst },
                ClassificacaoTributariaImposto = new AvalaraCodigo { Codigo = r.ClassTrib },
                ValorBaseTributo = r.TaxBase,
                ValorTributoBruto = r.IbsCbs.Cbs.Amount,
            },
        };

        if (r.SelectiveTax is { } selective)
        {
            impostos.Add(new AvalaraImposto
            {
                Imposto = new AvalaraCodigo { Codigo = "IS" },
                SituacaoTributariaImposto = new AvalaraCodigo { Codigo = selective.Cst },
                ClassificacaoTributariaImposto = new AvalaraCodigo { Codigo = selective.ClassTrib },
                ValorBaseTributo = selective.TaxBase,
                ValorTributoBruto = selective.Amount,
            });
        }

        return impostos;
    }
}
