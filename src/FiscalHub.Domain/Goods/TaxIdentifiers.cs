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
    private const int RootLength = 8;

    /// <summary>O CNPJ ou o CPF sem pontuação, com tudo o mais na ordem em que veio.</summary>
    public static string Normalize(string value) => new([.. value.Where(c => c is not ('.' or '/' or '-' or ' '))]);

    /// <summary>
    /// A raiz do CNPJ normalizado: os 8 primeiros caracteres, como texto (change <c>company-root-in-directory</c>, D1). Ela
    /// identifica a empresa; os estabelecimentos da mesma raiz são as filiais dela. Nunca número, nem "8 dígitos": o CNPJ
    /// alfanumérico tem raiz alfanumérica. Com menos de 8 caracteres, volta o valor inteiro.
    /// </summary>
    public static string Root(string normalized) => normalized.Length > RootLength ? normalized[..RootLength] : normalized;

    /// <summary>
    /// Os dois documentos normalizados são da mesma empresa: a mesma raiz, comparação ordinal. É a regra única do hub para
    /// "este estabelecimento é desta empresa" — o diretório, o escopo e a guarda da descoberta. A empresa continua gravada e
    /// mostrada pelo CNPJ completo; só a comparação é pela raiz. Um lado vazio não é empresa: não casa com nada, como a
    /// igualdade de antes.
    /// </summary>
    public static bool IsSameCompany(string a, string b)
        => a.Length > 0 && b.Length > 0 && string.Equals(Root(a), Root(b), StringComparison.Ordinal);
}
