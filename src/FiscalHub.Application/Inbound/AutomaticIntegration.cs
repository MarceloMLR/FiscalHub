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
        => Scans(profile, scanningOrigins)
            && ChangeFeedPollSettings.TryParse(profile!.InboundSettings, out ChangeFeedPollSettings? poll)
            && poll.Enabled;   // settings ilegíveis contam como desligada: o selo não afirma o que não se lê

    /// <summary>
    /// Se o adapter de entrada do tenant varre, ou seja, tem um feed de mudanças registrado no host, qualquer que seja o
    /// <c>poll</c>. É o que decide se o selo aparece (verde ligado, vermelho desligado) ou some (design D12 da change
    /// establishment-and-readable-dashboard). Sem perfil, não varre.
    /// </summary>
    public static bool Scans(TenantConnectorProfile? profile, IEnumerable<string> scanningOrigins)
        => profile is not null && scanningOrigins.Contains(profile.InboundAdapter, StringComparer.Ordinal);
}
