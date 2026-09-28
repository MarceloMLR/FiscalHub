using FiscalHub.Application.Metadata;
using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a extração de empresa/filial/data da NF-e: pelo estabelecimento próprio e pela data fiscal quando a origem
/// os informa (D365), e pela derivação do emitente e pela data do <c>dhEmi</c> no fuso dele quando não (XML).
/// </summary>
public class GoodsInvoiceMetadataExtractorTests
{
    [Fact]
    public void Extracts_company_branch_and_date_from_issuer_cnpj()
    {
        var invoice = Invoice(issuer: "12345678000290", recipient: "98765432000110");

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal("12345678", meta.CompanyCode);            // raiz do CNPJ
        Assert.Equal("0002", meta.BranchCode);                 // ordem do estabelecimento (filial 02)
        Assert.Equal(new DateOnly(2026, 6, 1), meta.ReferenceDate);
        Assert.Equal("123", meta.DocumentNumber);              // nNF
        Assert.Equal("55", meta.DocumentModel);
    }

    [Fact]
    public void Third_party_note_is_grouped_by_the_own_establishment_and_not_the_supplier()
    {
        // Nota de entrada: o fornecedor emite, o estabelecimento próprio recebe.
        var invoice = Invoice(issuer: "12345678000199", recipient: "44278225000180") with
        {
            Issuance = Issuance.ThirdParty,
            Establishment = new Establishment { TaxId = "44278225000180", Code = "Matriz" },
        };

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal("44278225000180", meta.CompanyCode);   // o CNPJ completo do estabelecimento, não a raiz do fornecedor
        Assert.Equal("Matriz", meta.BranchCode);            // o código do estabelecimento na origem
    }

    [Fact]
    public void Own_issued_note_is_grouped_by_the_own_establishment()
    {
        var invoice = Invoice(issuer: "44278225000260", recipient: "72458488000106") with
        {
            Issuance = Issuance.Own,
            Establishment = new Establishment { TaxId = "44278225000260", Code = "SP-01" },
        };

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal("44278225000260", meta.CompanyCode);
        Assert.Equal("SP-01", meta.BranchCode);
    }

    [Fact]
    public void Empty_establishment_code_leaves_the_branch_empty_instead_of_deriving_it()
    {
        var invoice = Invoice(issuer: "12345678000199", recipient: "44278225000180") with
        {
            Establishment = new Establishment { TaxId = "44278225000180", Code = "" },
        };

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal("44278225000180", meta.CompanyCode);
        Assert.Equal("", meta.BranchCode);   // nunca a ordem do CNPJ de outra parte ao lado da empresa própria
    }

    [Fact]
    public void Fiscal_date_defines_the_day_even_when_the_utc_issue_instant_is_the_next_day()
    {
        // 22:30 de 2026-08-07 em Brasília é 01:30 de 2026-08-08 em UTC, que é como o F&O guarda a data e hora.
        var invoice = Invoice(issuer: "44278225000180", recipient: "72458488000106") with
        {
            IssueDate = new DateTimeOffset(2026, 8, 8, 1, 30, 0, TimeSpan.Zero),
            FiscalDate = new DateOnly(2026, 8, 7),
        };

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal(new DateOnly(2026, 8, 7), meta.ReferenceDate);
    }

    [Theory]
    [InlineData("2026-06-01T22:30:00-03:00")]   // depois das 21h de Brasília: em UTC já seria 2026-06-02
    [InlineData("2026-06-01T23:30:00-04:00")]   // Manaus: em Brasília e em UTC já seria 2026-06-02
    public void Without_fiscal_date_the_day_is_the_issue_date_in_the_issuer_offset(string dhEmi)
    {
        var invoice = Invoice(issuer: "12345678000190", recipient: "98765432000110") with
        {
            IssueDate = DateTimeOffset.Parse(dhEmi, System.Globalization.CultureInfo.InvariantCulture),
        };

        DocumentMetadata meta = new GoodsInvoiceMetadataExtractor().Extract(invoice);

        Assert.Equal(new DateOnly(2026, 6, 1), meta.ReferenceDate);
    }

    private static GoodsInvoice Invoice(string issuer, string recipient) => new()
    {
        AccessKey = "35260612345678000290550010000001231000000123",
        Model = "55",
        Series = "1",
        Number = "123",
        IssueDate = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
        Issuer = new Party { TaxId = issuer, Name = "Emitente" },
        Recipient = new Party { TaxId = recipient, Name = "Destinatário" },
        Items = [],
        TotalAmount = 100m,
    };
}
