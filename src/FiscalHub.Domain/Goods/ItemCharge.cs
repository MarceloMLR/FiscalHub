namespace FiscalHub.Domain.Goods;

/// <summary>
/// Encargo de um item (frete, seguro, outras despesas), com os tributos e retenções que incidem sobre ele.
/// Fica no item porque a nota o apresenta por item.
/// </summary>
public sealed record ItemCharge
{
    /// <summary>Número do encargo no item.</summary>
    public required int Number { get; init; }

    /// <summary>Tipo do encargo.</summary>
    public required ChargeKind Kind { get; init; }

    /// <summary>Valor do encargo.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Descrição, quando houver.</summary>
    public string? Description { get; init; }

    /// <summary>Tributos sobre o encargo.</summary>
    public IReadOnlyList<TaxLine> Taxes { get; init; } = [];

    /// <summary>Retenções sobre o encargo — nunca somadas aos tributos.</summary>
    public IReadOnlyList<TaxLine> Withholdings { get; init; } = [];
}

/// <summary>Tipo de encargo do item, no vocabulário da NF-e (vFrete, vSeg, vOutro).</summary>
public enum ChargeKind
{
    Freight,
    Insurance,
    Other,
}
