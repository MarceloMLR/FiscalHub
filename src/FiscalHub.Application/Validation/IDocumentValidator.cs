namespace FiscalHub.Application.Validation;

/// <summary>
/// Valida se um documento está apto à INTEGRAÇÃO (completude e mapeabilidade) — não valida
/// tributo nem estrutura XSD. Devolve os problemas em vez de lançar, para o pipeline registrá-los.
/// </summary>
public interface IDocumentValidator<TDocument>
{
    ValidationResult Validate(TDocument document);
}
