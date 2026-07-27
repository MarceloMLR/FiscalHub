using FiscalHub.Application.Auth;
using FiscalHub.Application.Integrations;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Leitura das execuções de integração para o painel, escopada por tenant.</summary>
internal sealed class SqlExecutionQueries : IExecutionQueries
{
    private readonly ProcessingDbContext _db;
    private readonly ITenantContext _tenant;

    public SqlExecutionQueries(ProcessingDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ExecutionSummary>> ListRecentAsync(int max, CancellationToken ct = default)
        => await _db.IntegrationExecutions
            .Where(e => e.TenantId == _tenant.TenantId)
            .OrderByDescending(e => e.Id)   // mais recentes primeiro
            .Take(max)
            .Select(e => new ExecutionSummary
            {
                Id = e.Id,
                Mode = e.Mode,
                CompanyCode = e.CompanyCode,
                BranchCode = e.BranchCode,
                PeriodStart = e.PeriodStart,
                PeriodEnd = e.PeriodEnd,
                DiscoveredCount = e.DiscoveredCount,
                RunAt = e.CreatedAt,
            })
            .ToListAsync(ct);
}
