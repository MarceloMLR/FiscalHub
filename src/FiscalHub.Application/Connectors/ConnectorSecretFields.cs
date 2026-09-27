namespace FiscalHub.Application.Connectors;

/// <summary>
/// A regra de nomes dos campos de segredo nas settings de conector (ADR-0027). Olha o nome do campo, e não o schema de
/// um adapter: vale para as três settings, em qualquer nível do JSON. O nome é comparado sem distinguir maiúscula e
/// sem <c>_</c> ou <c>-</c>.
/// <list type="bullet">
///   <item><b>Campo de escrita</b> (<c>clientSecret</c>, <c>password</c>…): o valor, que a tela manda e o servidor
///   grava no cofre. Nunca é persistido.</item>
///   <item><b>Campo de referência</b> (<c>*Ref</c>): o <c>kv:&lt;nome&gt;</c> que o servidor grava no lugar. Nunca vem
///   da requisição, e nunca volta na leitura.</item>
/// </list>
/// </summary>
public static class ConnectorSecretFields
{
    private const string ReferenceSuffix = "ref";

    private static readonly HashSet<string> WriteFieldNames =
        ["clientsecret", "secret", "password", "senha", "apikey", "token", "accesstoken"];

    public static bool IsWriteField(string name) => WriteFieldNames.Contains(Normalize(name));

    public static bool IsReferenceField(string name)
    {
        string normalized = Normalize(name);
        return normalized.Length > ReferenceSuffix.Length && normalized.EndsWith(ReferenceSuffix, StringComparison.Ordinal);
    }

    /// <summary>O nome do campo sem maiúsculas, <c>_</c> e <c>-</c>: <c>CLIENT_SECRET</c> → <c>clientsecret</c>.</summary>
    public static string Normalize(string name) => new([.. name.ToLowerInvariant().Where(c => c is not ('_' or '-'))]);

    /// <summary>O campo de referência de um campo de escrita: <c>clientSecret</c> → <c>clientSecretRef</c>.</summary>
    public static string ReferenceOf(string writeField) => writeField + "Ref";

    /// <summary>O campo de escrita de um campo de referência: <c>clientSecretRef</c> → <c>clientSecret</c>.</summary>
    public static string WriteFieldOf(string referenceField) => referenceField[..^ReferenceSuffix.Length].TrimEnd('_', '-');
}
