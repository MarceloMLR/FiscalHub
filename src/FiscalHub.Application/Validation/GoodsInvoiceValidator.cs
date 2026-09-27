using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Validation;

/// <summary>
/// Validação de integração da NF-e de mercadoria (modelo 55). Julga só o que impede a requisição de existir, e
/// nenhum destino recebe uma nota sem item (ADR-0026). Chave, CFOP, NCM, grupo IBS/CBS, CST e cClassTrib são
/// conteúdo fiscal ou formato de conteúdo: seguem para a plataforma, que responde.
/// </summary>
public sealed class GoodsInvoiceValidator : IDocumentValidator<GoodsInvoice>
{
    public ValidationResult Validate(GoodsInvoice document)
        => document.Items.Count == 0
            ? ValidationResult.Invalid(["A nota não possui itens."])
            : ValidationResult.Valid();
}
