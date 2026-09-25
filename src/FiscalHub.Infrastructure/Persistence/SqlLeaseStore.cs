using FiscalHub.Application.Coordination;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Implementação de <see cref="ILeaseStore"/> em EF Core. Cada operação é um <c>UPDATE</c> condicional
/// numa instrução só (atômico em SQL Server e SQLite); o <c>INSERT</c> só acontece na primeira vez de um
/// recurso, e a PK decide quem chegou primeiro.
/// </summary>
internal sealed class SqlLeaseStore : ILeaseStore
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlLeaseStore(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> TryAcquireAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        long now = _clock.GetUtcNow().UtcTicks;
        long expires = now + ttl.Ticks;

        // Livre (liberado/vencido) ou já meu: toma.
        int taken = await _db.Leases
            .Where(l => l.Resource == resource && (l.Owner == owner || l.ExpiresTicks <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Owner, owner)
                .SetProperty(l => l.ExpiresTicks, expires), ct);
        if (taken == 1)
        {
            return true;
        }

        // Existe e está com outro dono válido.
        if (await _db.Leases.AnyAsync(l => l.Resource == resource, ct))
        {
            return false;
        }

        // Primeira vez deste recurso. Se outra réplica inseriu no meio, a PK recusa: ela ganhou.
        var row = new LeaseRow { Resource = resource, Owner = owner, ExpiresTicks = expires };
        _db.Leases.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
        finally
        {
            // O contexto é do escopo e compartilhado com outros stores: não deixar a linha rastreada.
            _db.Entry(row).State = EntityState.Detached;
        }
    }

    public async Task<bool> RenewAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        // Quem manda é o dono gravado: se outro tomou (depois de vencer), o dono mudou e a renovação falha.
        long expires = _clock.GetUtcNow().UtcTicks + ttl.Ticks;
        int renewed = await _db.Leases
            .Where(l => l.Resource == resource && l.Owner == owner)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresTicks, expires), ct);
        return renewed == 1;
    }

    public async Task ReleaseAsync(string resource, string owner, CancellationToken ct = default)
    {
        await _db.Leases
            .Where(l => l.Resource == resource && l.Owner == owner)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Owner, (string?)null)
                .SetProperty(l => l.ExpiresTicks, 0L), ct);
    }
}
