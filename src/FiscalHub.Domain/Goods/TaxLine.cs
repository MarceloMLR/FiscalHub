namespace FiscalHub.Domain.Goods;

/// <summary>
/// Um tributo de um item ou encargo, como a nota o apresenta: tipo, CST, base, alíquota e valor. Serve aos
/// tributos fora do grupo da Reforma (ICMS, IPI, PIS, COFINS, II…) e às retenções. IBS/CBS ficam em
/// <see cref="Reform.ReformTaxes"/>.
/// </summary>
public sealed record TaxLine
{
    /// <summary>Tipo do tributo.</summary>
    public required TaxKind Kind { get; init; }

    /// <summary>Código de situação tributária, quando houver.</summary>
    public string? Cst { get; init; }

    /// <summary>Base de cálculo.</summary>
    public required decimal TaxBase { get; init; }

    /// <summary>Alíquota, em percentual.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Valor do tributo.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Parcela da base isenta.</summary>
    public decimal ExemptBase { get; init; }

    /// <summary>Parcela da base em "outras" (nem tributada, nem isenta).</summary>
    public decimal OtherBase { get; init; }
}

/// <summary>
/// Tipos de tributo nacionais fora do grupo IBS/CBS. Um para um com os tipos da origem, sem fusão (fundir,
/// ex. INSS retido com INSS, seria regra inventada).
/// </summary>
public enum TaxKind
{
    Icms,
    IcmsSt,
    IcmsDiff,
    Ipi,
    Pis,
    Cofins,

    /// <summary>Imposto de importação (II).</summary>
    ImportTax,

    Iss,
    Irrf,
    Inss,
    InssRetained,
    InssCprb,
    Csll,

    /// <summary>Outro tributo que a origem classifica como tal.</summary>
    Other,
}
