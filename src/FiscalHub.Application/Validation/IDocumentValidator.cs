namespace FiscalHub.Application.Validation;

/// <summary>
/// Valida se um documento pode virar requisição para QUALQUER destino — só estrutura, nunca conteúdo fiscal (CST,
/// base, alíquota, grupo ausente) nem formato de conteúdo, que são julgados pela plataforma de compliance
/// (ADR-0026). O que é exigência do contrato de um destino é conferido pelo adapter de saída dele. Devolve os
/// problemas em vez de lançar, para o pipeline registrá-los.
/// </summary>
public interface IDocumentValidator<TDocument>
{
    ValidationResult Validate(TDocument document);
}
