using FiscalHub.Application.Validation;
using FiscalHub.Domain.Goods;
using FiscalHub.Domain.Goods.Reform;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a validação de integração da NF-e de mercadoria (TDD). O hub julga só o que impede a requisição de
/// existir; conteúdo fiscal e formato de conteúdo seguem para a plataforma, que responde (ADR-0026).
/// </summary>
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
    public void Invoice_without_items_is_rejected_with_its_reason()
    {
        GoodsInvoice invoice = SampleInvoice() with { Items = [] };

        ValidationResult result = _validator.Validate(invoice);

        Assert.False(result.IsValid);
        Assert.Equal(["A nota não possui itens."], result.Problems);
    }

    // Conteúdo fiscal e formato de conteúdo não são julgados aqui: cada caso abaixo segue para o envio.

    [Fact]
    public void Item_without_the_reform_group_passes()
        => AssertPasses(SampleInvoice() with { Items = [SampleItem() with { ReformTaxes = null }] });

    [Fact]
    public void Item_with_the_reform_group_but_without_class_trib_passes()
    {
        GoodsInvoiceItem item = SampleItem();
        AssertPasses(SampleInvoice() with { Items = [item with { ReformTaxes = item.ReformTaxes! with { ClassTrib = "", Cst = "" } }] });
    }

    [Fact]
    public void Empty_access_key_passes()
        => AssertPasses(SampleInvoice() with { AccessKey = "" });

    [Fact]
    public void Access_key_with_43_digits_passes()
        => AssertPasses(SampleInvoice() with { AccessKey = "3526061234567800019055001000000123100000012" });

    [Fact]
    public void Item_without_ncm_passes()
        => AssertPasses(SampleInvoice() with { Items = [SampleItem() with { Ncm = "" }] });

    [Theory]
    [InlineData("")]     // representabilidade como número é conferida pelo adapter de saída, que conhece o contrato
    [InlineData("12")]
    [InlineData("510")]
    public void Cfop_format_is_not_judged_by_the_validator(string cfop)
        => AssertPasses(SampleInvoice() with { Items = [SampleItem() with { Cfop = cfop }] });

    private void AssertPasses(GoodsInvoice invoice)
    {
        ValidationResult result = _validator.Validate(invoice);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
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
