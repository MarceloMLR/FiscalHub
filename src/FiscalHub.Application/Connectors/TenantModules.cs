namespace FiscalHub.Application.Connectors;

/// <summary>
/// Os módulos de integração que o tenant tem, e que montam a barra lateral do dashboard (change
/// <c>module-navigation-and-integration-panel</c>, D2). Desde a change <c>platform-account-and-connectors</c> (D7), eles
/// não são uma lista gravada no tenant: são a união dos módulos ligados nos conectores dele, e a forma do <c>/info</c> não
/// muda.
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
    /// Os módulos do tenant: a união dos ligados nos conectores dele, na ordem da barra lateral, sem repetir — qualquer que
    /// seja a ordem dos conectores. Sem perfil, sem conector, ou sem módulo ligado em nenhum, o <see cref="Default"/>.
    /// </summary>
    public static IReadOnlyList<string> Of(TenantConnectorProfile? profile)
    {
        var on = (profile?.Accounts ?? []).SelectMany(a => a.Connectors).SelectMany(c => c.Modules)
            .Select(m => m.Module).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<string> modules = [.. Known.Where(on.Contains)];
        return modules.Count == 0 ? Default : modules;
    }
}
