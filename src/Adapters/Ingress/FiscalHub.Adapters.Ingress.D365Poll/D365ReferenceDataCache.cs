using System.Collections.Concurrent;
using System.Text.Json;

namespace FiscalHub.Adapters.Ingress.D365Poll;

/// <summary>
/// Cadastros de referência do D365 em memória (design D13), fora do custo por documento: chave (tenant, entidade,
/// RecId) — por tenant mesmo que dois apontem o mesmo ambiente, porque credencial e permissão são dele. Expiração
/// absoluta sobre o <see cref="TimeProvider"/> e varredura dos vencidos a cada TTL, para a memória não crescer com
/// entrada morta. Não encontrado não entra; RecId 0 é FK vazia e nem consulta. Singleton.
/// </summary>
internal sealed class D365ReferenceDataCache
{
    private readonly ConcurrentDictionary<(string Tenant, string EntitySet, long RecId), Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _clock;
    private long _nextSweepTicks;

    public D365ReferenceDataCache(D365AssemblyOptions options, TimeProvider clock)
    {
        _ttl = options.ReferenceDataTtl;
        _clock = clock;
        _nextSweepTicks = (clock.GetUtcNow() + _ttl).UtcTicks;
    }

    /// <summary>Devolve o registro do cadastro, do cache ou do <paramref name="load"/>. <c>null</c> = FK vazia ou não encontrado.</summary>
    public async Task<JsonElement?> GetAsync(
        string tenantId, string entitySet, long recId, Func<CancellationToken, Task<JsonElement?>> load, CancellationToken ct)
    {
        if (recId == 0)
        {
            return null;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        var key = (tenantId, entitySet, recId);
        if (_entries.TryGetValue(key, out Entry? cached) && cached.ExpiresAt > now)
        {
            return cached.Row;
        }

        JsonElement? row = await load(ct);
        if (row is { } found)
        {
            _entries[key] = new Entry(found, now + _ttl);
        }

        Sweep(now);
        return row;
    }

    private void Sweep(DateTimeOffset now)
    {
        long next = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextSweepTicks, (now + _ttl).UtcTicks, next) != next)
        {
            return;
        }

        foreach ((var key, Entry entry) in _entries)
        {
            if (entry.ExpiresAt <= now)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(JsonElement Row, DateTimeOffset ExpiresAt);
}
