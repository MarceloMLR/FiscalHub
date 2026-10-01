using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Inbound;

/// <summary>
/// O painel da integração automática: o que o coletor registrou no cursor do tenant, e o <c>startFrom</c> gravado.
/// <see cref="Cursor"/> nulo = o coletor ainda não passou por este tenant.
/// </summary>
public sealed record AutomaticIntegrationPanel(ChangeFeedCursor? Cursor, DateTimeOffset? StartFrom);

/// <summary>
/// A leitura do painel da integração automática (change <c>module-navigation-and-integration-panel</c>, D6), sempre do
/// tenant do usuário logado. O cursor guarda o diagnóstico que hoje só o banco mostra (a última verificação, as falhas
/// seguidas, o último erro, a marca). O <c>startFrom</c> vem do perfil pelo mesmo parser do coletor, só como leitura:
/// sem marca, é dele que a primeira passada começa, e vê-lo evita descobrir o ponto de partida pelo silêncio.
/// </summary>
public sealed class AutomaticIntegrationPanelQuery
{
    private readonly IConnectorProfileStore _profiles;
    private readonly IChangeFeedCursorStore _cursors;
    private readonly IEnumerable<IDocumentChangeFeed> _feeds;
    private readonly ITenantContext _tenant;

    public AutomaticIntegrationPanelQuery(
        IConnectorProfileStore profiles, IChangeFeedCursorStore cursors, IEnumerable<IDocumentChangeFeed> feeds, ITenantContext tenant)
    {
        _profiles = profiles;
        _cursors = cursors;
        _feeds = feeds;
        _tenant = tenant;
    }

    /// <summary>O painel do tenant logado; <c>null</c> quando ele não tem perfil, ou o adapter de entrada não varre.</summary>
    public async Task<AutomaticIntegrationPanel?> GetAsync(CancellationToken ct = default)
    {
        TenantConnectorProfile? profile = await _profiles.GetAsync(_tenant.TenantId, ct);
        if (!AutomaticIntegration.Scans(profile, _feeds.Select(f => f.Origin)))
        {
            return null;
        }

        // A origem do feed casa com o nome do adapter de entrada (ADR-0025).
        ChangeFeedCursor? cursor = await _cursors.GetAsync(_tenant.TenantId, profile!.InboundAdapter, ct);
        DateTimeOffset? startFrom = ChangeFeedPollSettings.TryParse(profile.InboundSettings, out ChangeFeedPollSettings? poll) ? poll.StartFrom : null;
        return new AutomaticIntegrationPanel(cursor, startFrom);
    }
}
