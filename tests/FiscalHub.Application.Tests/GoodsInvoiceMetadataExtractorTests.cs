using FiscalHub.Application.Metadata;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Tests;

/// <summary>Especifica a extração de empresa/filial/data da NF-e a partir do CNPJ do emitente.</summary>
public class GoodsInvoiceMetadataExtractorTests
{
    [Fact]
    public void Extracts_company_branch_and_date_from_issuer_cnpj()
    {
        var invoice = new GoodsInvoice
        {
            AccessKey = "35260612345678000290550010000001231000000123",
            Model = "55",
            Series = "1",
            Number = "123",
            IssueDate = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
            Issuer = new Party { TaxId = "12345678000290", Name = "Empresa Filial 02" },
            Recipient = new Party { TaxId = "98765432000110", Name = "Cliente" },
            Items = [],
            TotalAmount = 100m,
        };

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal("12345678", meta.CompanyCode);            // raiz do CNPJ
        Assert.Equal("0002", meta.BranchCode);                 // ordem do estabelecimento (filial 02)
        Assert.Equal(new DateOnly(2026, 6, 1), meta.ReferenceDate);
    }
}
