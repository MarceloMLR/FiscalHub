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

    /// <summary>
    /// Tributos da Reforma (IBS/CBS/IS) do item. <c>null</c> = a nota não traz o grupo (ex.: nota anterior à
    /// Reforma) — ausente, e não zerado; a validação de integração rejeita.
    /// </summary>
    public ReformTaxes? ReformTaxes { get; init; }

    /// <summary>Tributos do item fora do grupo da Reforma (ICMS, IPI, PIS, COFINS, II…).</summary>
    public IReadOnlyList<TaxLine> Taxes { get; init; } = [];

    /// <summary>Retenções do item — separadas dos tributos para nunca serem somadas a eles.</summary>
    public IReadOnlyList<TaxLine> Withholdings { get; init; } = [];

    /// <summary>Encargos do item (frete, seguro, outras despesas), com os tributos deles.</summary>
    public IReadOnlyList<ItemCharge> Charges { get; init; } = [];
}
