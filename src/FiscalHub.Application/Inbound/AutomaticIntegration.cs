using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// Se a integração automática do tenant está ligada: o adapter de entrada varre (há um feed de mudanças para a origem
/// dele) e o <c>poll.enabled</c> das settings está ligado. É o que o <c>/info</c> e o selo mostram. É derivado do perfil,
/// e não gravado em outro lugar (ADR-0029): o coletor lê o mesmo <c>poll.enabled</c>.
/// </summary>
public static class AutomaticIntegration
{
    /// <param name="profile">O perfil do tenant; sem perfil, desligada.</param>
    /// <param name="scanningOrigins">As origens dos feeds de mudanças registrados (<see cref="IDocumentChangeFeed.Origin"/>).</param>
    public static bool IsOn(TenantConnectorProfile? profile, IEnumerable<string> scanningOrigins)
        => profile is not null
            && scanningOrigins.Contains(profile.InboundAdapter, StringComparer.Ordinal)
            && ChangeFeedPollSettings.TryParse(profile.InboundSettings, out ChangeFeedPollSettings? poll)
            && poll.Enabled;   // settings ilegíveis contam como desligada: o selo não afirma o que não se lê
}
