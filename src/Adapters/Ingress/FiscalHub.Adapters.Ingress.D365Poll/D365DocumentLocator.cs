using System.Globalization;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Locator de um documento do D365, contrato entre o feed e a montagem (ADR-0025):
/// <c>d365/&lt;dataAreaId&gt;/&lt;FiscalDocumentRecId&gt;</c>. O RecId é a chave primária do cabeçalho. Formato antigo
/// (voucher no lugar do RecId) ou de outra origem falha na leitura, antes de qualquer chamada ao F&amp;O.
/// </summary>
internal readonly record struct D365DocumentLocator(string Company, long FiscalDocumentRecId)
{
    private const string Expected = "d365/<dataAreaId>/<FiscalDocumentRecId>";

    public static D365DocumentLocator Parse(string locator)
    {
        string[] parts = locator.Split('/');
        if (parts.Length != 3 || parts[0] != "d365" || parts[1].Length == 0
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long recId) || recId <= 0)
        {
            throw new FormatException($"Locator do D365 inválido: '{locator}'. Esperado {Expected}, com o RecId inteiro e positivo.");
        }

        return new D365DocumentLocator(Uri.UnescapeDataString(parts[1]), recId);
    }
}
