using System.Diagnostics.CodeAnalysis;

namespace FiscalHub.Application.Connectors;

/// <summary>
/// A referência a um segredo no cofre, <c>kv:&lt;nome&gt;</c> (ADR-0019, ADR-0027). É a única forma persistida de um
/// segredo nas settings do perfil: o valor fica no cofre. O nome segue a regra do Key Vault — de 1 a 127 caracteres,
/// só letras ASCII, dígitos e hífen —, e a mesma regra vale em todos os ambientes.
/// </summary>
public static class SecretReference
{
    public const string Prefix = "kv:";

    private const int MaxNameLength = 127;

    /// <summary>O nome referenciado, se o valor for uma referência válida.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = value[Prefix.Length..];
        if (!IsValidName(candidate))
        {
            return false;
        }

        name = candidate;
        return true;
    }

    /// <summary>A referência <c>kv:&lt;nome&gt;</c> para um nome válido.</summary>
    public static string Of(string name)
        => IsValidName(name) ? Prefix + name : throw new ArgumentException($"Nome de segredo inválido para o cofre: '{name}'.", nameof(name));

    public static bool IsValidName(string name)
        => name.Length is > 0 and <= MaxNameLength && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
