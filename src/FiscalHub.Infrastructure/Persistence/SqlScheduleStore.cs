using FiscalHub.Application.Auth;
using FiscalHub.Application.Integrations;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Implementação de <see cref="IScheduleStore"/> em EF Core.</summary>
internal sealed class SqlScheduleStore : IScheduleStore
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ITenantContext _tenant;

    public SqlScheduleStore(ProcessingDbContext db, TimeProvider clock, ITenantContext tenant)
    {
        _db = db;
        _clock = clock;
        _tenant = tenant;
    }

    public async Task<int> CreateAsync(ScheduledIntegration schedule, CancellationToken ct = default)
    {
        var row = new ScheduledIntegrationRow
        {
            Mode = schedule.Mode,
            TenantId = schedule.TenantId,
            CompanyCode = schedule.CompanyCode,
            BranchCode = schedule.BranchCode,
            PeriodStart = schedule.PeriodStart,
            PeriodEnd = schedule.PeriodEnd,
            NextRunTicks = schedule.NextRunAt.UtcTicks,
            Active = true,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.ScheduledIntegrations.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(ScheduledIntegration schedule, CancellationToken ct = default)
    {
        // Só edita agendamento do próprio tenant (defesa contra id de outro tenant).
        ScheduledIntegrationRow? row = await _db.ScheduledIntegrations
            .FirstOrDefaultAsync(s => s.Id == schedule.Id && s.TenantId == _tenant.TenantId, ct);
        if (row is null)
        {
            return false;
        }

        row.Mode = schedule.Mode;
        row.CompanyCode = schedule.CompanyCode;
        row.BranchCode = schedule.BranchCode;
        row.PeriodStart = schedule.PeriodStart;
        row.PeriodEnd = schedule.PeriodEnd;
        row.NextRunTicks = schedule.NextRunAt.UtcTicks;
        row.Active = true;   // salvar reativa (reprograma o próximo disparo)

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReactivateAsync(int id, DateTimeOffset nextRunAt, CancellationToken ct = default)
    {
        ScheduledIntegrationRow? row = await _db.ScheduledIntegrations
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == _tenant.TenantId, ct);
        if (row is null)
        {
            return false;
        }

        row.Active = true;
        row.NextRunTicks = nextRunAt.UtcTicks;   // volta pra fila com o próximo disparo recalculado
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ScheduledIntegration>> ListAsync(CancellationToken ct = default)
    {
        // Lista da UI: só os agendamentos do tenant logado. (O agendador, que roda os vencidos, usa
        // ListDueAsync — esse é do sistema, varre todos os tenants.)
        List<ScheduledIntegrationRow> rows = await _db.ScheduledIntegrations
            .Where(s => s.TenantId == _tenant.TenantId)
            .OrderByDescending(s => s.Id)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ScheduledIntegration>> ListDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        long nowTicks = now.UtcTicks;   // comparação por inteiro traduz em qualquer provider
        List<ScheduledIntegrationRow> rows = await _db.ScheduledIntegrations
            .Where(s => s.Active && s.NextRunTicks <= nowTicks)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task RescheduleAsync(int id, DateTimeOffset? nextRunAt, CancellationToken ct = default)
    {
        ScheduledIntegrationRow? row = await _db.ScheduledIntegrations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
        {
            return;
        }

        if (nextRunAt is null)
        {
            row.Active = false;   // agendamento único: cumpriu, sai de cena
        }
        else
        {
            row.NextRunTicks = nextRunAt.Value.UtcTicks;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(int id, CancellationToken ct = default)
    {
        ScheduledIntegrationRow? row = await _db.ScheduledIntegrations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.Active = false;
        await _db.SaveChangesAsync(ct);
    }

    private static ScheduledIntegration Map(ScheduledIntegrationRow s) => new()
    {
        Id = s.Id,
        Mode = s.Mode,
        TenantId = s.TenantId,
        CompanyCode = s.CompanyCode,
        BranchCode = s.BranchCode,
        PeriodStart = s.PeriodStart,
        PeriodEnd = s.PeriodEnd,
        NextRunAt = new DateTimeOffset(s.NextRunTicks, TimeSpan.Zero),
        Active = s.Active,
    };
}
