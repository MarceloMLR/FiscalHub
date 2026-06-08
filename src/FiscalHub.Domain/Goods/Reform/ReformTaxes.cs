namespace FiscalHub.Domain.Goods.Reform;

/// <summary>
/// Grupo UB da NT 2025.002: tributos da Reforma (IBS, CBS e IS) por item da NF-e. É cidadão de
/// primeira classe do modelo — o conector transporta esses dados da origem até o destino SEM PERDA
/// ("reform-ready").
///
/// Importante: aqui NÃO há cálculo nem apuração de imposto — isso é trabalho do sistema de
/// compliance (Avalara etc.). Os tributos antigos (ICMS/PIS/COFINS/IPI) convivem durante a
/// transição e serão modelados à parte.
/// </summary>
public sealed record ReformTaxes
{
    /// <summary>CST do IBS/CBS — Código de Situação Tributária (3 dígitos).</summary>
    public required string Cst { get; init; }

    /// <summary>cClassTrib — Código de Classificação Tributária (6 dígitos).</summary>
    public required string ClassTrib { get; init; }

    /// <summary>Base de cálculo compartilhada por IBS e CBS no item (campo vBC).</summary>
    public required decimal TaxBase { get; init; }

    /// <summary>Subgrupo IBS/CBS do item.</summary>
    public required IbsCbs IbsCbs { get; init; }

    /// <summary>Subgrupo Imposto Seletivo (IS), quando aplicável ao item.</summary>
    public SelectiveTax? SelectiveTax { get; init; }
}

/// <summary>
/// Subgrupo IBS/CBS do item. O IBS é dividido em parcela estadual (UF) e municipal, conforme os
/// grupos gIBSUF e gIBSMun da NT 2025.002.
/// </summary>
public sealed record IbsCbs
{
    /// <summary>Parcela estadual do IBS (gIBSUF).</summary>
    public required TaxShare IbsState { get; init; }

    /// <summary>Parcela municipal do IBS (gIBSMun).</summary>
    public required TaxShare IbsMunicipality { get; init; }

    /// <summary>Valor total do IBS no item (vIBS = vIBSUF + vIBSMun).</summary>
    public required decimal IbsTotalAmount { get; init; }

    /// <summary>CBS do item (gCBS).</summary>
    public required TaxShare Cbs { get; init; }
}

/// <summary>
/// Alíquota e valor de uma parcela de tributo. Reutilizado para UF, Município e CBS, já que os três
/// têm a mesma forma. Usar decimal — nunca double — para valores monetários e alíquotas.
/// </summary>
public sealed record TaxShare
{
    /// <summary>Alíquota aplicada, em percentual (ex.: pIBSUF, pIBSMun, pCBS).</summary>
    public required decimal Rate { get; init; }

    /// <summary>Valor do tributo na parcela (ex.: vIBSUF, vIBSMun, vCBS).</summary>
    public required decimal Amount { get; init; }
}

/// <summary>
/// Imposto Seletivo (IS) do item: tem situação e classificação tributária próprias, base, alíquota
/// e quantidade tributável (subgrupo do Grupo UB).
/// </summary>
public sealed record SelectiveTax
{
    /// <summary>CST do IS.</summary>
    public required string Cst { get; init; }

    /// <summary>cClassTrib do IS.</summary>
    public required string ClassTrib { get; init; }

    /// <summary>Base de cálculo do IS no item.</summary>
    public required decimal TaxBase { get; init; }

    /// <summary>Alíquota do IS, em percentual.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Unidade de medida tributável (quando o IS é por quantidade).</summary>
    public string? TaxableUnit { get; init; }

    /// <summary>Quantidade tributável (quando o IS é por quantidade).</summary>
    public decimal? TaxableQuantity { get; init; }

    /// <summary>Valor do Imposto Seletivo no item (vIS).</summary>
    public required decimal Amount { get; init; }
}
