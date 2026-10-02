namespace FiscalHub.Domain.Goods;

/// <summary>
/// A forma única do CNPJ e do CPF no hub (spec tax-identifier-normalization): sem ponto, barra, hífen e espaço, com as
/// letras e a caixa como vieram. O CNPJ alfanumérico (inscrições novas desde julho de 2026) atravessa a descoberta, a
/// montagem, o diretório, o card e o envio com o mesmo valor. Não confere tamanho nem dígito verificador e não converte a
/// caixa: isso é conteúdo fiscal, e quem o julga é a plataforma (ADR-0026). Para documento só com dígitos, o resultado é o
/// mesmo que a regra de "só dígitos" dava.
/// </summary>
public static class TaxIdentifiers
{
    /// <summary>O CNPJ ou o CPF sem pontuação, com tudo o mais na ordem em que veio.</summary>
    public static string Normalize(string value) => new([.. value.Where(c => c is not ('.' or '/' or '-' or ' '))]);
}
