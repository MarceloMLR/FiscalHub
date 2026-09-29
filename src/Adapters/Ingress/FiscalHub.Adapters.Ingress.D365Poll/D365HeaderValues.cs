using System.Globalization;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Leituras do cabeçalho da <c>FSFiscalDocumentBRs</c> que a descoberta e a montagem fazem igual, num lugar só, para os dois
/// caminhos não divergirem (design D1 e D4 da change establishment-and-readable-dashboard): o dia fiscal e o CNPJ do
/// estabelecimento.
/// </summary>
internal static class D365HeaderValues
{
    /// <summary>
    /// O dia do <c>FiscalDocumentDate</c>: um campo de data do F&amp;O, sem hora e sem fuso, que o OData devolve como
    /// <c>yyyy-MM-ddT12:00:00Z</c>. O dia é a parte da data, como veio, sem conversão de fuso. Valor ausente ou fora do
    /// formato é falha: documento autorizado ou cancelado tem data, e um dia inventado esconderia a quebra.
    /// </summary>
    public static DateOnly FiscalDay(string? literal)
        => literal is { Length: >= 10 } && DateOnly.TryParseExact(literal.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : throw new FormatException($"FiscalDocumentDate inválido no F&O: '{literal}'.");

    /// <summary>CNPJ/CPF só com dígitos (o F&amp;O o devolve formatado, <c>442782250001-80</c>).</summary>
    public static string Digits(string value) => new([.. value.Where(char.IsAsciiDigit)]);
}
