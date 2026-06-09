namespace FiscalHub.Domain.Goods;

/// <summary>Emitente ou destinatário, como consta no documento.</summary>
public sealed record Party
{
    /// <summary>CNPJ ou CPF.</summary>
    public required string TaxId { get; init; }

    /// <summary>Razão social ou nome.</summary>
    public required string Name { get; init; }

    /// <summary>Inscrição estadual, quando houver.</summary>
    public string? StateRegistration { get; init; }

    /// <summary>Código IBGE do município.</summary>
    public string? MunicipalityCode { get; init; }
}
