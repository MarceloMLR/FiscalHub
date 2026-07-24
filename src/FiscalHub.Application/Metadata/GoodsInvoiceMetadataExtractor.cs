using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Metadata;

/// <summary>
/// Deriva empresa/filial/data da NF-e a partir do emitente: os 8 primeiros dígitos do CNPJ são a
/// empresa (raiz), os dígitos 9–12 são a filial (ordem do estabelecimento; "0001" = matriz), e a
/// data de referência é a de emissão. O "código interno" (Avalara e afins) entra como config depois.
/// </summary>
public sealed class GoodsInvoiceMetadataExtractor : IDocumentMetadataExtractor<GoodsInvoice>
{
    public DocumentMetadata Extract(GoodsInvoice document)
    {
        string cnpj = new string(document.Issuer.TaxId.Where(char.IsDigit).ToArray());
        string company = cnpj.Length >= 8 ? cnpj[..8] : cnpj;
        string branch = cnpj.Length >= 12 ? cnpj.Substring(8, 4) : "0001";

        return new DocumentMetadata
        {
            CompanyCode = company,
            BranchCode = branch,
            ReferenceDate = DateOnly.FromDateTime(document.IssueDate.Date),
            DocumentNumber = document.Number,
            DocumentModel = document.Model,
        };
    }
}
