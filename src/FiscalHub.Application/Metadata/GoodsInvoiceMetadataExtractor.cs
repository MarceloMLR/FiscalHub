using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Metadata;

/// <summary>
/// Deriva empresa/filial/data da NF-e.
/// <list type="bullet">
///   <item><b>Empresa e filial:</b> pelo estabelecimento próprio, quando a origem o informa (D365): o CNPJ completo é a
///   empresa, e o código do estabelecimento é a filial — numa nota de terceiro, é o estabelecimento que escritura, e não o
///   fornecedor. Sem ele (XML, andaime de dev), pelo emitente, sobre o CNPJ sem a pontuação e com as letras
///   (<see cref="TaxIdentifiers"/>): os 8 primeiros caracteres são a empresa (raiz) e os caracteres 9–12, a filial
///   ("0001" = matriz).</item>
///   <item><b>Dia:</b> a data fiscal que o próprio documento registra, no fuso de quem emitiu, sem conversão — nem UTC,
///   nem um fuso fixo. No D365, o <see cref="GoodsInvoice.FiscalDate"/>; no XML, a data do <c>dhEmi</c> no fuso que ele
///   traz. A nota ignorada, que não chega aqui, usa o mesmo dia pela descoberta.</item>
/// </list>
/// </summary>
public sealed class GoodsInvoiceMetadataExtractor : IDocumentMetadataExtractor<GoodsInvoice>
{
    public DocumentMetadata Extract(GoodsInvoice document)
    {
        (string company, string branch) = document.Establishment is { } own
            ? (own.TaxId, own.Code)
            : FromIssuer(document.Issuer);

        return new DocumentMetadata
        {
            CompanyCode = company,
            BranchCode = branch,
            // DateTimeOffset.Date é a data no fuso do próprio valor: o do dhEmi, e nunca convertida.
            ReferenceDate = document.FiscalDate ?? DateOnly.FromDateTime(document.IssueDate.Date),
            DocumentNumber = document.Number,
            DocumentModel = document.Model,
        };
    }

    private static (string Company, string Branch) FromIssuer(Party issuer)
    {
        string cnpj = TaxIdentifiers.Normalize(issuer.TaxId);
        return (TaxIdentifiers.Root(cnpj), cnpj.Length >= 12 ? cnpj.Substring(8, 4) : "0001");
    }
}
