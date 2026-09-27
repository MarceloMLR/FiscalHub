namespace FiscalHub.Adapters.Ingress.D365Poll.Tests;

/// <summary>
/// Contrato do Locator com o feed (spec d365-document-assembly): <c>d365/&lt;dataAreaId&gt;/&lt;FiscalDocumentRecId&gt;</c>.
/// Qualquer outra forma falha antes de tocar o F&amp;O, com o formato esperado na mensagem.
/// </summary>
public class D365DocumentLocatorTests
{
    [Fact]
    public void Valid_locator_gives_company_and_rec_id()
    {
        D365DocumentLocator locator = D365DocumentLocator.Parse("d365/brmf/5637148912");

        Assert.Equal("brmf", locator.Company);
        Assert.Equal(5637148912L, locator.FiscalDocumentRecId);
    }

    [Fact]
    public void Company_segment_is_url_decoded()
    {
        Assert.Equal("br mf", D365DocumentLocator.Parse("d365/br%20mf/35637156582").Company);
    }

    [Theory]
    [InlineData("d365/brmf/BRMF21-10000027")]   // formato antigo: voucher no lugar do RecId
    [InlineData("d365/brmf/0")]
    [InlineData("d365/brmf/-5")]
    [InlineData("d365/brmf/")]
    [InlineData("d365/brmf")]
    [InlineData("d365//5637148912")]
    [InlineData("d365/brmf/5637148912/extra")]
    [InlineData("nfe/nfe-exemplo.xml")]          // locator de outra origem
    public void Anything_else_fails_citing_the_expected_format(string raw)
    {
        var ex = Assert.Throws<FormatException>(() => D365DocumentLocator.Parse(raw));

        Assert.Contains("d365/<dataAreaId>/<FiscalDocumentRecId>", ex.Message);
        Assert.Contains(raw, ex.Message);
    }
}
