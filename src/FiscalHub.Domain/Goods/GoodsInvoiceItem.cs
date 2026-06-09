using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Domain.Goods;

/// <summary>Item de uma <see cref="GoodsInvoice"/>, com dados do produto e os tributos da Reforma.</summary>
public sealed record GoodsInvoiceItem
{
    /// <summary>Número sequencial do item (nItem).</summary>
    public required int Number { get; init; }

    /// <summary>Código do produto na origem (cProd).</summary>
    public required string ProductCode { get; init; }

    /// <summary>Descrição do produto (xProd).</summary>
    public required string Description { get; init; }

    /// <summary>NCM do produto.</summary>
    public required string Ncm { get; init; }

    /// <summary>CFOP da operação.</summary>
    public required string Cfop { get; init; }

    /// <summary>Quantidade comercializada.</summary>
    public required decimal Quantity { get; init; }

    /// <summary>Valor unitário.</summary>
    public required decimal UnitAmount { get; init; }

    /// <summary>Valor total do item.</summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>Tributos da Reforma (IBS/CBS/IS) do item.</summary>
    public required ReformTaxes ReformTaxes { get; init; }
}
