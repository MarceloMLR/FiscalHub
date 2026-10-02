using FiscalHub.Domain.Goods;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a forma única do CNPJ e do CPF no hub (spec tax-identifier-normalization): sem ponto, barra, hífen e espaço,
/// com as letras e a caixa como vieram. O CNPJ alfanumérico, válido para inscrições novas desde julho de 2026, não pode
/// perder as letras em silêncio. Fica aqui porque não há projeto de teste do Domain, e este já o alcança.
/// </summary>
public class TaxIdentifiersTests
{
    [Theory]
    [InlineData("442782250001-80", "44278225000180")]       // como o F&O devolve
    [InlineData("44.278.225/0001-80", "44278225000180")]    // a máscara completa
    [InlineData("44278225000180", "44278225000180")]        // já sem pontuação
    [InlineData("123.456.789-09", "12345678909")]           // CPF
    [InlineData(" 44 278 225 0001 80 ", "44278225000180")]  // espaços
    public void Removes_only_the_punctuation_of_a_numeric_document(string value, string expected)
        => Assert.Equal(expected, TaxIdentifiers.Normalize(value));

    [Fact]
    public void Keeps_the_letters_of_an_alphanumeric_cnpj()
    {
        string normalized = TaxIdentifiers.Normalize("12.ABC.345/01DE-35");

        Assert.Equal("12ABC34501DE35", normalized);
        Assert.NotEqual("123450135", normalized);   // a regra de "só dígitos" de antes
    }

    [Fact]
    public void Keeps_the_case_as_it_came()
        => Assert.Equal("12abc34501de35", TaxIdentifiers.Normalize("12abc34501de35"));

    [Fact]
    public void Keeps_any_other_character_in_order()
        => Assert.Equal("12_AB34501DE35", TaxIdentifiers.Normalize("12_AB.345/01DE-35"));

    [Fact]
    public void Two_alphanumeric_cnpjs_with_the_same_digits_stay_apart()
        => Assert.NotEqual(TaxIdentifiers.Normalize("12ABC34501DE35"), TaxIdentifiers.Normalize("12XYZ34501DE35"));

    [Fact]
    public void Does_not_judge_the_length()
        => Assert.Equal("123", TaxIdentifiers.Normalize("1.2-3"));

    [Fact]
    public void Empty_stays_empty()
        => Assert.Equal(string.Empty, TaxIdentifiers.Normalize(string.Empty));
}
