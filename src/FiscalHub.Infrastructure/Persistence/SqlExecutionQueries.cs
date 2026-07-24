using FiscalHub.Application.Integrations;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Leitura das execuções de integração para o painel.</summary>
internal sealed class SqlExecutionQueries : IExecutionQueries
{
    private readonly ProcessingDbContext _db;

    public SqlExecutionQueries(ProcessingDbContext db) => _db = db;

    public async Task<IReadOnlyList<ExecutionSummary>> ListRecentAsync(int max, CancellationToken ct = default)
        => await _db.IntegrationExecutions
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
