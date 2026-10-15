namespace FiscalHub.Application.Connectors;

/// <summary>
/// O vocabulário dos módulos de integração: os valores aceitos e a ordem em que eles aparecem (change
/// <c>module-navigation-and-integration-panel</c>, D2). Eles não são uma lista gravada no tenant: são os ligados nos
/// conectores dele (change <c>platform-account-and-connectors</c>, D7).
/// <para>Desde a change <c>connector-navigation-and-admin-json</c> (D6), o <c>/info</c> não responde mais a união deles
/// no nível do tenant: ele responde a árvore de conectores, e os módulos de cada conector são ordenados por
/// <see cref="Ordered"/>. O <see cref="Of(TenantConnectorProfile?)"/> continua servindo a leitura do perfil
/// (<c>GET /connector</c>), que é outra resposta.</para>
/// <para>Isso é apresentação, e não permissão: a API continua respondendo para um módulo escondido. Restringir de fato é
/// outra fatia.</para>
/// <para>Contábil e Inventário são lugares reservados: outro domínio, e cada um vira uma fatia própria. Ligá-los não
/// entrega integração nenhuma.</para>
/// </summary>
public static class TenantModules
{
    public const string Fiscal = "Fiscal";

    public const string Accounting = "Contabil";

    public const string Inventory = "Inventario";

    /// <summary>Os aceitos, na ordem da barra lateral, que é também a ordem gravada.</summary>
    public static IReadOnlyList<string> Known { get; } = [Fiscal, Accounting, Inventory];

    /// <summary>Sem módulo ligado em conector algum, o tenant tem só o Fiscal: o comportamento de antes dos módulos.</summary>
    public static IReadOnlyList<string> Default { get; } = [Fiscal];

    /// <summary>
    /// Os módulos dados na ordem da barra lateral, sem repetir e só os aceitos — qualquer que seja a ordem em que vieram.
    /// É a ordem de um conector na árvore do <c>/info</c>, e a mesma que a união do tenant usa.
    /// </summary>
    public static IReadOnlyList<string> Ordered(IEnumerable<string> modules)
    {
        var on = modules.ToHashSet(StringComparer.Ordinal);
        return [.. Known.Where(on.Contains)];
    }

    /// <summary>
    /// Os módulos do tenant: a união dos ligados nos conectores dele, na ordem da barra lateral, sem repetir — qualquer que
    /// seja a ordem dos conectores. Sem perfil, sem conector, ou sem módulo ligado em nenhum, o <see cref="Default"/>.
    /// </summary>
    public static IReadOnlyList<string> Of(TenantConnectorProfile? profile)
    {
        IReadOnlyList<string> modules = Ordered(
            (profile?.Accounts ?? []).SelectMany(a => a.Connectors).SelectMany(c => c.Modules).Select(m => m.Module));
        return modules.Count == 0 ? Default : modules;
    }
}
