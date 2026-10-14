namespace FiscalHub.Application.Connectors;

/// <summary>
/// O que o <c>/info</c> responde a qualquer papel do tenant logado, para montar a barra lateral e os selos. O ambiente é o
/// da instância, e nunca o do perfil nem um padrão (change <c>platform-account-and-connectors</c>, D6). A integração
/// automática é derivada do perfil (ADR-0029), e os módulos, apresentação.
/// </summary>
public sealed record InstanceInfo(string Environment, bool AutomaticIntegration, bool InboundScans, IReadOnlyList<string> Modules)
{
    /// <param name="instance">O ambiente da instância.</param>
    /// <param name="profile">O perfil do tenant logado; sem perfil, valem os padrões dos selos e dos módulos.</param>
    /// <param name="scanningOrigins">As origens dos feeds de mudanças registrados no host.</param>
    public static InstanceInfo For(IInstanceEnvironment instance, TenantConnectorProfile? profile, IEnumerable<string> scanningOrigins)
    {
        string[] origins = [.. scanningOrigins];
        return new InstanceInfo(
            instance.Name,
            Inbound.AutomaticIntegration.IsOn(profile, origins),
            Inbound.AutomaticIntegration.Scans(profile, origins),
            TenantModules.Of(profile));
    }
}
