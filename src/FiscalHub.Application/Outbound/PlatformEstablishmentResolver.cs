using System.Collections.Concurrent;
using FiscalHub.Application.Connectors;

namespace FiscalHub.Application.Outbound;

/// <summary>A janela da listagem de estabelecimentos da plataforma, da seção <c>PlatformEstablishments</c> do host (D8).</summary>
public sealed class PlatformEstablishmentOptions
{
    public const string Section = "PlatformEstablishments";

    /// <summary>
    /// A validade da listagem, por tenant e ambiente: a janela de despacho. Um cadastro novo na plataforma aparece em
    /// minutos, e um lote de milhares de notas usa uma listagem.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Quanto tempo a recusa permanente da listagem fica lembrada: o mesmo padrão da recusa da credencial.</summary>
    public TimeSpan RefusalHold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Zero ou negativo impede o host de subir: uma validade zero seria a listagem por nota. É melhor falhar na subida que
    /// descobrir pela conta de chamadas.
    /// </summary>
    public void Validate()
    {
        if (CacheDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(CacheDuration)} precisa ser positivo (veio {CacheDuration}): com validade zero, a plataforma seria listada a cada nota.");
        }

        if (RefusalHold <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(RefusalHold)} precisa ser positivo (veio {RefusalHold}): sem ele, cada nota seria uma tentativa com a listagem recusada.");
        }
    }
}

/// <summary>
/// O de/para do estabelecimento pela plataforma (change <c>platform-establishment-resolution</c>, D2 e D8): escolhe a
/// listagem pelo adapter de saída do perfil, pela comparação exata do nome, e devolve o índice da plataforma para o tenant
/// e ambiente. A regra é a mesma para qualquer destino que liste; quem escreve o motivo da recusa é o adapter, que sabe o
/// vocabulário da plataforma.
/// <list type="bullet">
///   <item><b>A janela:</b> a listagem fica guardada por tenant e ambiente pela <see cref="PlatformEstablishmentOptions.CacheDuration"/>,
///   com vencimento absoluto. Dentro dela, toda resolução usa a mesma listagem.</item>
///   <item><b>A busca única:</b> uma listagem em voo por chave. Quem chega durante ela recebe o mesmo desfecho, sucesso ou
///   falha. Ela não leva o <see cref="CancellationToken"/> de quem a começou: cada um aguarda com o seu.</item>
///   <item><b>A falha:</b> não fica guardada, salvo a recusa permanente (<see cref="DispatchRejectedException"/>), lembrada
///   pela <see cref="PlatformEstablishmentOptions.RefusalHold"/> — sem isso, uma credencial que não lista viraria uma
///   tentativa por nota, o padrão que bloqueia conta (ADR-0027 §7).</item>
///   <item><b>O salvar do perfil</b> esquece a listagem, a busca em voo e a recusa do tenant, em todos os ambientes. Uma
///   busca que já estava em voo entrega o resultado a quem esperava por ela, mas não vira a listagem guardada.</item>
/// </list>
/// Singleton, e por processo, como o token da Avalara.
/// </summary>
public sealed class PlatformEstablishmentResolver : IConnectorProfileObserver
{
    private readonly IEnumerable<IPlatformEstablishmentListing> _listings;
    private readonly PlatformEstablishmentOptions _options;
    private readonly TimeProvider _clock;

    private readonly ConcurrentDictionary<Key, Cached> _cache = new();
    private readonly ConcurrentDictionary<Key, Refusal> _refusals = new();
    private readonly ConcurrentDictionary<Key, Task<PlatformEstablishmentIndex>> _inFlight = new();
    private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);

    public PlatformEstablishmentResolver(
        IEnumerable<IPlatformEstablishmentListing> listings, PlatformEstablishmentOptions options, TimeProvider clock)
    {
        options.Validate();
        _listings = listings;
        _options = options;
        _clock = clock;
    }

    /// <summary>
    /// O índice da plataforma para o tenant e o ambiente ativo do perfil. Sem listagem declarada pelo adapter de saída,
    /// <see cref="PlatformEstablishmentIndex.Unsupported"/>, sem chamada. A recusa da listagem chega como
    /// <see cref="DispatchRejectedException"/>; a indisponibilidade, como a exceção que veio.
    /// </summary>
    public async Task<PlatformEstablishmentIndex> GetAsync(TenantConnectorProfile profile, CancellationToken ct = default)
    {
        IPlatformEstablishmentListing? listing = _listings.FirstOrDefault(
            l => string.Equals(l.Adapter, profile.OutboundAdapter, StringComparison.Ordinal));
        if (listing is null)
        {
            return PlatformEstablishmentIndex.Unsupported;
        }

        var key = new Key(profile.TenantId, profile.Environment.ToLowerInvariant());
        DateTimeOffset now = _clock.GetUtcNow();
        if (_cache.TryGetValue(key, out Cached? cached) && cached.ExpiresAt > now)
        {
            return cached.Index;
        }

        if (_refusals.TryGetValue(key, out Refusal? refusal))
        {
            if (refusal.Until > now)
            {
                throw new DispatchRejectedException(refusal.Reason);
            }

            _refusals.TryRemove(new KeyValuePair<Key, Refusal>(key, refusal));
        }

        // Só quem põe a própria tarefa no dicionário começa a busca; os outros aguardam a que já está lá. A geração é lida
        // antes: um salvar no meio do caminho faz a busca não virar a listagem guardada.
        long generation = Generation(key.TenantId);
        var flight = new TaskCompletionSource<PlatformEstablishmentIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PlatformEstablishmentIndex> shared = _inFlight.GetOrAdd(key, flight.Task);
        if (ReferenceEquals(shared, flight.Task))
        {
            _ = ListAsync(key, listing, profile, generation, flight);
        }

        return await shared.WaitAsync(ct);
    }

    public Task ProfileSavedAsync(string tenantId, CancellationToken ct = default)
    {
        _generations.AddOrUpdate(tenantId, 1, (_, generation) => generation + 1);
        foreach (Key key in _cache.Keys.Concat(_refusals.Keys).Concat(_inFlight.Keys).Where(k => k.TenantId == tenantId).ToList())
        {
            _cache.TryRemove(key, out _);
            _refusals.TryRemove(key, out _);
            _inFlight.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    // Nunca lança: o desfecho vai para a tarefa compartilhada. O guardado vem antes de tirar a busca do dicionário, para
    // que quem chegue no meio ache a listagem, e não comece outra.
    private async Task ListAsync(
        Key key, IPlatformEstablishmentListing listing, TenantConnectorProfile profile, long generation,
        TaskCompletionSource<PlatformEstablishmentIndex> flight)
    {
        try
        {
            PlatformEstablishmentIndex index = PlatformEstablishmentIndex.From(await listing.ListAsync(profile, CancellationToken.None));
            if (Generation(key.TenantId) == generation)
            {
                _cache[key] = new Cached(index, _clock.GetUtcNow() + _options.CacheDuration);
            }

            Leave(key, flight.Task);
            flight.SetResult(index);
        }
        catch (DispatchRejectedException rejected)
        {
            if (Generation(key.TenantId) == generation)
            {
                _refusals[key] = new Refusal(rejected.Reason, _clock.GetUtcNow() + _options.RefusalHold);
            }

            Leave(key, flight.Task);
            flight.SetException(rejected);
        }
        catch (Exception ex)
        {
            Leave(key, flight.Task);
            flight.SetException(ex);
        }
    }

    // Tira só a própria busca: depois de um salvar, a que está lá pode ser outra.
    private void Leave(Key key, Task<PlatformEstablishmentIndex> flight)
        => _inFlight.TryRemove(new KeyValuePair<Key, Task<PlatformEstablishmentIndex>>(key, flight));

    private long Generation(string tenantId) => _generations.TryGetValue(tenantId, out long generation) ? generation : 0;

    private sealed record Key(string TenantId, string Environment);

    private sealed record Cached(PlatformEstablishmentIndex Index, DateTimeOffset ExpiresAt);

    private sealed record Refusal(string Reason, DateTimeOffset Until);
}
