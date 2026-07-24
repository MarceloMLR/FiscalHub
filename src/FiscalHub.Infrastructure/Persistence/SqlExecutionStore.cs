using FiscalHub.Application.Integrations;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Implementação de <see cref="IExecutionStore"/> em EF Core.</summary>
internal sealed class SqlExecutionStore : IExecutionStore
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlExecutionStore(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task RecordAsync(IntegrationExecution execution, CancellationToken ct = default)
    {
        _db.IntegrationExecutions.Add(new IntegrationExecutionRow
        {
            Mode = execution.Mode,
            TenantId = execution.TenantId,
            CompanyCode = execution.CompanyCode,
            BranchCode = execution.BranchCode,
            PeriodStart = execution.PeriodStart.ToString("yyyy-MM-dd"),
            PeriodEnd = execution.PeriodEnd.ToString("yyyy-MM-dd"),
            DiscoveredCount = execution.DiscoveredCount,
            ScheduleId = execution.ScheduleId,
            CreatedAt = _clock.GetUtcNow(),
        });

        await _db.SaveChangesAsync(ct);
    }
}
