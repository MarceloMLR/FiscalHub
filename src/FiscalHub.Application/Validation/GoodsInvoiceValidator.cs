using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Validation;

/// <summary>Validação de integração da NF-e de mercadoria (modelo 55).</summary>
public sealed class GoodsInvoiceValidator : IDocumentValidator<GoodsInvoice>
{
    public ValidationResult Validate(GoodsInvoice document)
    {
        var problems = new List<string>();

        if (document.AccessKey.Length != 44)
            problems.Add("Chave de acesso deve ter 44 dígitos.");

        if (document.Items.Count == 0)
            problems.Add("A nota não possui itens.");

        foreach (GoodsInvoiceItem item in document.Items)
        {
            if (!IsCfop(item.Cfop))
                problems.Add($"Item {item.Number}: CFOP inválido ou não mapeável ('{item.Cfop}').");

            if (string.IsNullOrWhiteSpace(item.Ncm))
                problems.Add($"Item {item.Number}: NCM ausente.");

            if (string.IsNullOrWhiteSpace(item.ReformTaxes.Cst))
                problems.Add($"Item {item.Number}: CST da reforma ausente.");

            if (string.IsNullOrWhiteSpace(item.ReformTaxes.ClassTrib))
                problems.Add($"Item {item.Number}: cClassTrib ausente.");
        }

        return problems.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(problems);
    }

    // Mapeabilidade básica: CFOP precisa ser 4 dígitos para ser traduzível ao destino.
    private static bool IsCfop(string value) => value.Length == 4 && value.All(char.IsDigit);
}
