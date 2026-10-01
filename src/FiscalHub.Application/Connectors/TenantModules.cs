using System.Diagnostics.CodeAnalysis;

namespace FiscalHub.Application.Connectors;

/// <summary>
/// Os módulos de integração que o tenant tem, e que montam a barra lateral do dashboard (change
/// <c>module-navigation-and-integration-panel</c>, D2). É dado do perfil, e não código: cada cliente contrata um conjunto.
/// <para>Isso é apresentação, e não permissão: a API continua respondendo para um módulo escondido. Restringir de fato é
/// outra fatia.</para>
/// <para>Contábil e Inventário são lugares reservados: outro domínio, e cada um vira uma fatia própria. Marcá-los não
/// entrega integração nenhuma.</para>
/// </summary>
public static class TenantModules
{
    public const string Fiscal = "Fiscal";

    public const string Accounting = "Contabil";

    public const string Inventory = "Inventario";

    /// <summary>Os aceitos, na ordem da barra lateral, que é também a ordem gravada.</summary>
    public static IReadOnlyList<string> Known { get; } = [Fiscal, Accounting, Inventory];

    /// <summary>Sem módulos gravados, o tenant tem só o Fiscal: o comportamento de antes dos módulos.</summary>
    public static IReadOnlyList<string> Default { get; } = [Fiscal];

    /// <summary>Os módulos do tenant; sem perfil, ou sem módulos gravados, o <see cref="Default"/>.</summary>
    public static IReadOnlyList<string> Of(TenantConnectorProfile? profile) => profile?.Modules ?? Default;

    /// <summary>
    /// Normaliza a lista pedida: cada valor na grafia aceita, sem repetição, na ordem da barra lateral. Recusa o valor
    /// desconhecido e a lista vazia: a ausência já quer dizer "só o Fiscal", e a lista vazia seria um segundo estado sem
    /// significado de produto.
    /// </summary>
    public static bool TryNormalize(IEnumerable<string> requested, out IReadOnlyList<string> modules, [NotNullWhen(false)] out string? problem)
    {
        modules = [];
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in requested)
        {
            string? known = Known.FirstOrDefault(k => string.Equals(k, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                problem = $"Módulo desconhecido: '{value}'. Os aceitos são {Accepted()}.";
                return false;
            }

            chosen.Add(known);
        }

        if (chosen.Count == 0)
        {
            problem = $"Marque pelo menos um módulo ({Accepted()}).";
            return false;
        }

        modules = [.. Known.Where(chosen.Contains)];
        problem = null;
        return true;
    }

    private static string Accepted() => $"{string.Join(", ", Known.Take(Known.Count - 1))} e {Known[^1]}";
}
