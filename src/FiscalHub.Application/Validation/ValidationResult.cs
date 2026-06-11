namespace FiscalHub.Application.Validation;

/// <summary>Resultado de uma validação de integração: válido, ou inválido com os problemas encontrados.</summary>
public sealed record ValidationResult
{
    public required bool IsValid { get; init; }

    public required IReadOnlyList<string> Problems { get; init; }

    public static ValidationResult Valid() => new() { IsValid = true, Problems = [] };

    public static ValidationResult Invalid(IReadOnlyList<string> problems)
        => new() { IsValid = false, Problems = problems };
}
