using FiscalHub.Application.Validation;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Application.Tests;

/// <summary>Especifica a validação de integração da NF-e de mercadoria (TDD).</summary>
public class GoodsInvoiceValidatorTests
{
    private readonly GoodsInvoiceValidator _validator = new();

    [Fact]
    public void Valid_invoice_passes()
    {
        ValidationResult result = _validator.Validate(SampleInvoice());

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Item_with_empty_cfop_is_invalid()
    {
        GoodsInvoice invoice = SampleInvoice() with { Items = [SampleItem() with { Cfop = "" }] };

        ValidationResult result = _validator.Validate(invoice);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("CFOP"));
    }

    [Fact]
    public void Item_with_malformed_cfop_is_invalid()
    {
        // CFOP não numérico / fora de 4 dígitos não é mapeável para o destino.
        GoodsInvoice invoice = SampleInvoice() with { Items = [SampleItem() with { Cfop = "12" }] };

        ValidationResult result = _validator.Validate(invoice);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Invoice_without_items_is_invalid()
    {
        GoodsInvoice invoice = SampleInvoice() with { Items = [] };

        ValidationResult result = _validator.Validate(invoice);

        Assert.False(result.IsValid);
    }

    private static GoodsInvoice SampleInvoice() => new()
    {
        AccessKey = "35260612345678000190550010000001231000000123",
        Model = "55",
        Series = "1",
        Number = "123",
        IssueDate = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
        Issuer = new Party { TaxId = "12345678000190", Name = "Emitente" },
        Recipient = new Party { TaxId = "98765432000110", Name = "Cliente" },
        TotalAmount = 100m,
        Items = [SampleItem()],
    };

    private static GoodsInvoiceItem SampleItem() => new()
    {
        Number = 1,
        ProductCode = "PROD-001",
        Description = "Produto de Teste",
        Ncm = "12345678",
        Cfop = "5102",
        Quantity = 1m,
        UnitAmount = 100m,
        TotalAmount = 100m,
        ReformTaxes = new ReformTaxes
        {
            Cst = "000",
            ClassTrib = "000001",
            TaxBase = 100m,
            IbsCbs = new IbsCbs
            {
                IbsState = new TaxShare { Rate = 8.5m, Amount = 8.5m },
                IbsMunicipality = new TaxShare { Rate = 2m, Amount = 2m },
                IbsTotalAmount = 10.5m,
                Cbs = new TaxShare { Rate = 0.9m, Amount = 0.9m },
            },
        },
    };
}
