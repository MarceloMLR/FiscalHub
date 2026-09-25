using FiscalHub.Application.Coordination;
using FiscalHub.Application.Inbound;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>
/// Implementação de <see cref="IChangeFeedCursorStore"/> em EF Core. Instantes em ticks UTC (ADR-0017).
/// O avanço da marca é uma instrução só, condicionada ao lease (<c>EXISTS</c> na tabela <c>Leases</c>) e
/// monotônica — é o fencing do ADR-0024: réplica que perdeu o lease não grava, e nada faz a marca regredir.
/// </summary>
internal sealed class SqlChangeFeedCursorStore : IChangeFeedCursorStore
{
    private const int MaxErrorLength = 500;

    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlChangeFeedCursorStore(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ChangeFeedCursor?> GetAsync(string tenantId, string origin, CancellationToken ct = default)
    {
        ChangeFeedCursorRow? row = await _db.ChangeFeedCursors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Origin == origin, ct);
        return row is null ? null : Map(row);
    }

    public async Task<ChangeFeedCursor> StartAsync(string tenantId, string origin, DateTimeOffset initialWatermark, CancellationToken ct = default)
    {
        long ticks = initialWatermark.UtcTicks;
        DateTimeOffset now = _clock.GetUtcNow();

        // Existe sem marca (nasceu de uma falha registrada): preenche. Com marca, não mexe.
        int filled = await _db.ChangeFeedCursors
            .Where(c => c.TenantId == tenantId && c.Origin == origin && c.WatermarkTicks == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.WatermarkTicks, ticks)
                .SetProperty(c => c.UpdatedAt, now), ct);

        if (filled == 0 && !await _db.ChangeFeedCursors.AnyAsync(c => c.TenantId == tenantId && c.Origin == origin, ct))
        {
            await TryInsertAsync(new ChangeFeedCursorRow
            {
                TenantId = tenantId,
                Origin = origin,
                WatermarkTicks = ticks,
                UpdatedAt = now,
            }, ct);
        }

        return (await GetAsync(tenantId, origin, ct))!;
    }

    public async Task<bool> TryAdvanceWatermarkAsync(
        string tenantId, string origin, DateTimeOffset watermark, LeaseClaim lease, CancellationToken ct = default)
    {
        long target = watermark.UtcTicks;
        DateTimeOffset now = _clock.GetUtcNow();
        long nowTicks = now.UtcTicks;

        // Uma instrução: UPDATE … WHERE marca < @w AND EXISTS (lease válido do dono). Renovar antes e gravar
        // depois só estreitaria a janela; aqui a posse do lease é conferida no mesmo passo da gravação.
        int advanced = await _db.ChangeFeedCursors
            .Where(c => c.TenantId == tenantId
                && c.Origin == origin
                && c.WatermarkTicks < target
                && _db.Leases.Any(l => l.Resource == lease.Resource && l.Owner == lease.Owner && l.ExpiresTicks > nowTicks))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.WatermarkTicks, target)
                .SetProperty(c => c.UpdatedAt, now), ct);
        return advanced == 1;
    }

    public async Task RecordSuccessAsync(string tenantId, string origin, DateTimeOffset polledAt, CancellationToken ct = default)
    {
        long polled = polledAt.UtcTicks;
        DateTimeOffset now = _clock.GetUtcNow();

        await _db.ChangeFeedCursors
            .Where(c => c.TenantId == tenantId && c.Origin == origin)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastPolledTicks, polled)
                .SetProperty(c => c.ConsecutiveFailures, 0)
                .SetProperty(c => c.LastError, (string?)null)
                .SetProperty(c => c.NotBeforeTicks, (long?)null)
                .SetProperty(c => c.UpdatedAt, now), ct);
    }

    public async Task RecordFailureAsync(
        string tenantId, string origin, DateTimeOffset polledAt, string error, DateTimeOffset? notBefore, CancellationToken ct = default)
    {
        string message = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        if (await IncrementFailureAsync(tenantId, origin, polledAt, message, notBefore, ct))
        {
            return;
        }

        // Primeira notícia deste par (ex.: settings inválidas antes do primeiro poll): cria sem marca.
        bool inserted = await TryInsertAsync(new ChangeFeedCursorRow
        {
            TenantId = tenantId,
            Origin = origin,
            LastPolledTicks = polledAt.UtcTicks,
            NotBeforeTicks = notBefore?.UtcTicks,
            ConsecutiveFailures = 1,
            LastError = message,
            UpdatedAt = _clock.GetUtcNow(),
        }, ct);

        if (!inserted)
        {
            // Outra réplica criou no meio: agora a linha existe.
            await IncrementFailureAsync(tenantId, origin, polledAt, message, notBefore, ct);
        }
    }

    private async Task<bool> IncrementFailureAsync(
        string tenantId, string origin, DateTimeOffset polledAt, string message, DateTimeOffset? notBefore, CancellationToken ct)
    {
        long polled = polledAt.UtcTicks;
        long? notBeforeTicks = notBefore?.UtcTicks;
        DateTimeOffset now = _clock.GetUtcNow();

        int updated = await _db.ChangeFeedCursors
            .Where(c => c.TenantId == tenantId && c.Origin == origin)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastPolledTicks, polled)
                .SetProperty(c => c.ConsecutiveFailures, c => c.ConsecutiveFailures + 1)
                .SetProperty(c => c.LastError, message)
                .SetProperty(c => c.NotBeforeTicks, notBeforeTicks)
                .SetProperty(c => c.UpdatedAt, now), ct);
        return updated == 1;
    }

    /// <summary>Insere; se o índice único recusar (corrida entre réplicas), devolve <c>false</c> sem deixar lixo rastreado.</summary>
    private async Task<bool> TryInsertAsync(ChangeFeedCursorRow row, CancellationToken ct)
    {
        _db.ChangeFeedCursors.Add(row);
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
            _db.Entry(row).State = EntityState.Detached;
        }
    }

    private static ChangeFeedCursor Map(ChangeFeedCursorRow r) => new()
    {
        TenantId = r.TenantId,
        Origin = r.Origin,
        Watermark = FromTicks(r.WatermarkTicks),
        LastPolledAt = FromTicks(r.LastPolledTicks),
        NotBefore = FromTicks(r.NotBeforeTicks),
        ConsecutiveFailures = r.ConsecutiveFailures,
        LastError = r.LastError,
    };

    private static DateTimeOffset? FromTicks(long? ticks) => ticks is null ? null : new DateTimeOffset(ticks.Value, TimeSpan.Zero);
}
