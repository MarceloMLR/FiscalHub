namespace FiscalHub.Application.Connectors;

/// <summary>
/// O escopo do segredo no perfil de conector: as settings de entrada, a conta de plataforma, as settings de um conector,
/// ou as de chamados. O nome do segredo começa por ele (change <c>platform-account-and-connectors</c>, D1).
/// </summary>
public enum ConnectorSettingsKind
{
    Inbound,
    Platform,
    Connector,
    Support,
}

/// <summary>
/// O nome, no cofre, do segredo de um campo das settings de conector. Quem deriva é o servidor, e nunca o cliente
/// (ADR-0027): <c>fh-{tenant}--{tipo}--{caminho}--{campo}</c>, em minúsculas. O separador é duplo porque os ids de
/// tenant têm hífen — com hífen simples, <c>fh-tenant-</c> seria prefixo de <c>fh-tenant-a-…</c>, e a checagem de dono
/// aceitaria o segredo de outro tenant. Nenhum segmento pode conter <c>--</c>, e por isso <c>fh-{tenant}--</c> é exato.
/// </summary>
public static class SecretNames
{
    /// <summary>O prefixo de todo segredo de conector. É o que a condição de acesso do cofre de produção restringe.</summary>
    public const string Prefix = "fh-";

    private const string Separator = "--";

    public static string For(string tenantId, ConnectorSettingsKind kind, IReadOnlyList<string> path, string field)
    {
        IEnumerable<string> segments = [kind.ToString(), .. path, field];
        string name = TenantPrefix(tenantId) + string.Join(Separator, segments.Select(Segment));

        return SecretReference.IsValidName(name)
            ? name
            : throw new ArgumentException($"O nome derivado passa de 127 caracteres ou foge da regra do cofre: '{name}'.");
    }

    /// <summary>O prefixo exato dos segredos do tenant: nenhum outro tenant tem um nome que comece por ele.</summary>
    public static string TenantPrefix(string tenantId) => Prefix + Segment(tenantId) + Separator;

    public static bool IsConnectorSecret(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool BelongsTo(string name, string tenantId) => name.StartsWith(TenantPrefix(tenantId), StringComparison.Ordinal);

    // Minúsculas (o cofre normaliza os nomes), só letras, dígitos e hífen, sem hífen nas pontas e sem o separador.
    private static string Segment(string value)
    {
        string segment = value.ToLowerInvariant();
        bool valid = segment.Length > 0
            && segment.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
            && segment[0] != '-'
            && segment[^1] != '-'
            && !segment.Contains(Separator, StringComparison.Ordinal);

        return valid
            ? segment
            : throw new ArgumentException(
                $"O segmento '{value}' não serve para nome de segredo: só letras, dígitos e hífen, sem hífen nas pontas e sem '--'.");
    }
}
