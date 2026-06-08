namespace FiscalHub.Domain.Mercadoria.Reforma;

/// <summary>
/// Grupo UB da NT 2025.002: tributos da Reforma (IBS, CBS e IS) por item da NF-e.
/// É cidadão de primeira classe do modelo — o conector transporta esses dados da
/// origem até o destino SEM PERDA ("reform-ready").
///
/// Importante: aqui NÃO há cálculo nem apuração de imposto — isso é trabalho do
/// sistema de compliance (Avalara etc.). Os tributos antigos (ICMS/PIS/COFINS/IPI)
/// convivem durante a transição e serão modelados à parte.
/// </summary>
public sealed record TributosReforma
{
    /// <summary>CST do IBS/CBS — Código de Situação Tributária (3 dígitos).</summary>
    public required string Cst { get; init; }

    /// <summary>cClassTrib — Código de Classificação Tributária (6 dígitos).</summary>
    public required string ClassificacaoTributaria { get; init; }

    /// <summary>Base de cálculo compartilhada por IBS e CBS no item (campo vBC).</summary>
    public required decimal BaseCalculo { get; init; }

    /// <summary>Subgrupo IBS/CBS do item.</summary>
    public required IbsCbs IbsCbs { get; init; }

    /// <summary>Subgrupo Imposto Seletivo (IS), quando aplicável ao item.</summary>
    public ImpostoSeletivo? ImpostoSeletivo { get; init; }
}

/// <summary>
/// Subgrupo IBS/CBS do item. O IBS é dividido em parcela estadual (UF) e municipal,
/// conforme os grupos gIBSUF e gIBSMun da NT 2025.002.
/// </summary>
public sealed record IbsCbs
{
    /// <summary>Parcela estadual do IBS (gIBSUF).</summary>
    public required ParcelaTributo IbsUf { get; init; }

    /// <summary>Parcela municipal do IBS (gIBSMun).</summary>
    public required ParcelaTributo IbsMunicipio { get; init; }

    /// <summary>Valor total do IBS no item (vIBS = vIBSUF + vIBSMun).</summary>
    public required decimal ValorIbsTotal { get; init; }

    /// <summary>CBS do item (gCBS).</summary>
    public required ParcelaTributo Cbs { get; init; }
}

/// <summary>
/// Alíquota e valor de uma parcela de tributo. Reutilizado para UF, Município e CBS,
/// já que os três têm a mesma forma (alíquota + valor). Usar decimal — nunca double —
/// para valores monetários e alíquotas.
/// </summary>
public sealed record ParcelaTributo
{
    /// <summary>Alíquota aplicada, em percentual (ex.: pIBSUF, pIBSMun, pCBS).</summary>
    public required decimal Aliquota { get; init; }

    /// <summary>Valor do tributo na parcela (ex.: vIBSUF, vIBSMun, vCBS).</summary>
    public required decimal Valor { get; init; }
}

/// <summary>
/// Imposto Seletivo (IS) do item: tem situação e classificação tributária próprias,
/// base, alíquota e quantidade tributável (subgrupo do Grupo UB).
/// </summary>
public sealed record ImpostoSeletivo
{
    /// <summary>CST do IS.</summary>
    public required string Cst { get; init; }

    /// <summary>cClassTrib do IS.</summary>
    public required string ClassificacaoTributaria { get; init; }

    /// <summary>Base de cálculo do IS no item.</summary>
    public required decimal BaseCalculo { get; init; }

    /// <summary>Alíquota do IS, em percentual.</summary>
    public required decimal Aliquota { get; init; }

    /// <summary>Unidade de medida tributável (quando o IS é por quantidade).</summary>
    public string? UnidadeTributavel { get; init; }

    /// <summary>Quantidade tributável (quando o IS é por quantidade).</summary>
    public decimal? QuantidadeTributavel { get; init; }

    /// <summary>Valor do Imposto Seletivo no item (vIS).</summary>
    public required decimal Valor { get; init; }
}
