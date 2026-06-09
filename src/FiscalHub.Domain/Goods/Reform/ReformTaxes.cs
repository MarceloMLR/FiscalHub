namespace FiscalHub.Domain.Goods.Reform;

/// <summary>Tributos da Reforma (IBS, CBS e IS) de um item — Grupo UB da NT 2025.002.</summary>
public sealed record ReformTaxes
{
    /// <summary>CST do IBS/CBS.</summary>
    public required string Cst { get; init; }

    /// <summary>Código de classificação tributária (cClassTrib).</summary>
    public required string ClassTrib { get; init; }

    /// <summary>Base de cálculo de IBS e CBS (vBC).</summary>
    public required decimal TaxBase { get; init; }

    /// <summary>Subgrupo IBS/CBS.</summary>
    public required IbsCbs IbsCbs { get; init; }

    /// <summary>Subgrupo do Imposto Seletivo, quando aplicável.</summary>
    public SelectiveTax? SelectiveTax { get; init; }
}

/// <summary>Subgrupo IBS/CBS de um item; o IBS é dividido em parcela estadual e municipal.</summary>
public sealed record IbsCbs
{
    /// <summary>Parcela estadual do IBS (gIBSUF).</summary>
    public required TaxShare IbsState { get; init; }

    /// <summary>Parcela municipal do IBS (gIBSMun).</summary>
    public required TaxShare IbsMunicipality { get; init; }

    /// <summary>Valor total do IBS (vIBS).</summary>
    public required decimal IbsTotalAmount { get; init; }

    /// <summary>CBS do item (gCBS).</summary>
    public required TaxShare Cbs { get; init; }
}

/// <summary>Alíquota e valor de uma parcela de tributo (IBS estadual, IBS municipal ou CBS).</summary>
public sealed record TaxShare
{
    /// <summary>Alíquota, em percentual.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Valor do tributo.</summary>
    public required decimal Amount { get; init; }
}

/// <summary>Imposto Seletivo (IS) de um item.</summary>
public sealed record SelectiveTax
{
    /// <summary>CST do IS.</summary>
    public required string Cst { get; init; }

    /// <summary>Código de classificação tributária do IS.</summary>
    public required string ClassTrib { get; init; }

    /// <summary>Base de cálculo.</summary>
    public required decimal TaxBase { get; init; }

    /// <summary>Alíquota, em percentual.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Unidade tributável, quando o IS é por quantidade.</summary>
    public string? TaxableUnit { get; init; }

    /// <summary>Quantidade tributável, quando o IS é por quantidade.</summary>
    public decimal? TaxableQuantity { get; init; }

    /// <summary>Valor do IS (vIS).</summary>
    public required decimal Amount { get; init; }
}
