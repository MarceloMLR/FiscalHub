namespace FiscalHub.Domain.Goods;

/// <summary>Endereço de uma parte, como consta no documento. Cada campo ausente fica nulo — nunca vazio inventado.</summary>
public sealed record Address
{
    /// <summary>Logradouro.</summary>
    public string? Street { get; init; }

    /// <summary>Número.</summary>
    public string? Number { get; init; }

    /// <summary>Bairro.</summary>
    public string? District { get; init; }

    /// <summary>CEP.</summary>
    public string? PostalCode { get; init; }
}
