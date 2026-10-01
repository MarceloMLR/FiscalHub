using FiscalHub.Application.Auth;
using FiscalHub.Application.Connectors;
using FiscalHub.Application.Coordination;

namespace FiscalHub.Application.Inbound;

public enum RewindStatus
{
    Rewound,

    /// <summary>O adapter de entrada do tenant não varre: não há marca para rebobinar.</summary>
    NotScanning,

    /// <summary>O coletor está lendo o tenant (o lease é dele), ou o lease se perdeu antes da gravação. Nada mudou.</summary>
    Busy,

    /// <summary>Sem cursor, ou cursor sem marca: a primeira passada parte do <c>startFrom</c>. Nada mudou.</summary>
    NoWatermark,

    /// <summary>O alvo não é anterior à marca, ou está no futuro. Nada mudou.</summary>
    Invalid,
}

/// <summary>O desfecho do rebobinamento. Com <see cref="RewindStatus.Rewound"/>, a marca de antes e a nova, para o log.</summary>
public sealed record RewindResult(RewindStatus Status, string Message, DateTimeOffset? Previous = null, DateTimeOffset? Watermark = null);

/// <summary>
/// O rebobinamento da marca pela tela (change <c>module-navigation-and-integration-panel</c>, D7). É o mesmo mecanismo do
/// coletor, e não um segundo:
/// <list type="bullet">
///   <item><b>o lease:</b> o mesmo recurso do coletor (<c>changefeed:{origem}:{tenant}</c>), com um dono próprio. Com o
///   coletor lendo, não rebobina;</item>
///   <item><b>o fencing:</b> a gravação é condicionada ao lease na mesma instrução (<see cref="IChangeFeedCursorStore.TryRewindWatermarkAsync"/>);</item>
///   <item><b>o registro de publicações:</b> o serviço não o esquece. A passada seguinte lê a marca menor que a última vista,
///   e o <see cref="ChangeFeedPublicationLog.BeginPull"/> zera o registro, na réplica que faz o poll — a mesma regra do
///   rebobinamento por SQL.</item>
/// </list>
/// Só para trás (avançar pularia notas em silêncio), e só num cursor que já tem marca: sem marca, a primeira passada parte
/// do <c>startFrom</c>, e a tela não cria um segundo jeito de a marca nascer.
/// </summary>
public sealed class ChangeFeedRewind
{
    /// <summary>Prazo do lease do rebobinamento: o bastante para ler e gravar o cursor, e curto para não segurar o coletor.</summary>
    public static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly IConnectorProfileStore _profiles;
    private readonly IChangeFeedCursorStore _cursors;
    private readonly ILeaseStore _leases;
    private readonly IEnumerable<IDocumentChangeFeed> _feeds;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public ChangeFeedRewind(
        IConnectorProfileStore profiles,
        IChangeFeedCursorStore cursors,
        ILeaseStore leases,
        IEnumerable<IDocumentChangeFeed> feeds,
        ITenantContext tenant,
        TimeProvider clock)
    {
        _profiles = profiles;
        _cursors = cursors;
        _leases = leases;
        _feeds = feeds;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<RewindResult> RewindAsync(DateTimeOffset target, CancellationToken ct = default)
    {
        string tenant = _tenant.TenantId;
        TenantConnectorProfile? profile = await _profiles.GetAsync(tenant, ct);
        if (!AutomaticIntegration.Scans(profile, _feeds.Select(f => f.Origin)))
        {
            return new RewindResult(RewindStatus.NotScanning, "Este ERP não tem integração automática.");
        }

        if (target > _clock.GetUtcNow())
        {
            return new RewindResult(RewindStatus.Invalid, "Escolha uma data no passado.");
        }

        // A origem do feed casa com o nome do adapter de entrada (ADR-0025).
        string origin = profile!.InboundAdapter;
        var lease = new LeaseClaim($"changefeed:{origin}:{tenant}", $"rewind:{Guid.NewGuid():N}");
        if (!await _leases.TryAcquireAsync(lease.Resource, lease.Owner, LeaseTtl, ct))
        {
            return Busy();
        }

        try
        {
            // Sob o lease, o coletor não avança a marca entre esta leitura e a gravação: o avanço exige o lease dele.
            ChangeFeedCursor? cursor = await _cursors.GetAsync(tenant, origin, ct);
            if (cursor?.Watermark is not { } current)
            {
                return new RewindResult(RewindStatus.NoWatermark, "Ainda não houve nenhuma busca para este tenant.");
            }

            if (target >= current)
            {
                return new RewindResult(RewindStatus.Invalid, "Escolha uma data anterior à última sincronização.");
            }

            if (!await _cursors.TryRewindWatermarkAsync(tenant, origin, target, lease, ct))
            {
                return Busy();   // o lease venceu entre a leitura e a gravação: o fencing recusou
            }

            return new RewindResult(RewindStatus.Rewound, "Busca reprogramada.", current, target);
        }
        finally
        {
            // Liberar sempre: senão o coletor do tenant fica parado até o prazo do lease vencer.
            await _leases.ReleaseAsync(lease.Resource, lease.Owner, CancellationToken.None);
        }
    }

    // As mensagens vão para a tela: sem "coletor" nem "marca", que são palavras do código (revisão de 2026-10-01).
    private static RewindResult Busy()
        => new(RewindStatus.Busy, "A integração automática está buscando notas agora. Tente novamente em alguns segundos.");
}
