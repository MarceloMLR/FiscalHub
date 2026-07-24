using FiscalHub.Application.Outbound;
using FiscalHub.Application.Queries;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Lado de leitura em EF Core: projeta o rastreio para a visão do dashboard.</summary>
internal sealed class SqlDocumentQueries : IDocumentQueries
{
    private readonly ProcessingDbContext _db;

    public SqlDocumentQueries(ProcessingDbContext db) => _db = db;

    public async Task<IReadOnlyList<DocumentSummary>> ListRecentAsync(int limit, CancellationToken ct = default)
        => await _db.ProcessedDocuments
            .OrderByDescending(d => d.Id)
            .Take(limit)
            .Select(d => new DocumentSummary
            {
                TenantId = d.TenantId,
                NaturalKey = d.NaturalKey,
                Type = d.Type,
                Status = d.Status,
                Attempts = d.Attempts,
                ExternalId = d.ExternalId,
                Reason = d.Reason,
                UpdatedAt = d.UpdatedAt,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, CancellationToken ct = default)
        => await _db.ProcessedDocuments
            .Where(d => d.CompanyCode != null && d.ReferenceDate != null)
            .GroupBy(d => new { d.CompanyCode, d.BranchCode, d.ReferenceDate, d.Type })
            .OrderByDescending(g => g.Key.ReferenceDate)
            .ThenBy(g => g.Key.CompanyCode)
            .ThenBy(g => g.Key.BranchCode)
            .Select(g => new DocumentGroup
            {
                CompanyCode = g.Key.CompanyCode!,
                BranchCode = g.Key.BranchCode ?? string.Empty,
                ReferenceDate = g.Key.ReferenceDate!,
                Type = g.Key.Type,
                Total = g.Count(),
                Finalizadas = g.Count(x => x.Status == IntegrationStatus.Confirmed),
                EmProcessamento = g.Count(x =>
                    x.Status == IntegrationStatus.Submitted || x.Status == IntegrationStatus.Pending),
                ComErro = g.Count(x =>
                    x.Status == IntegrationStatus.IntegrationError
                    || x.Status == IntegrationStatus.DeadLettered
                    || x.Status == IntegrationStatus.Unconfirmed),
            })
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(
        string companyCode, string branchCode, string referenceDate, CancellationToken ct = default)
        => await _db.ProcessedDocuments
            .Where(d => d.CompanyCode == companyCode && d.BranchCode == branchCode && d.ReferenceDate == referenceDate)
            .OrderByDescending(d => d.Id)
            .Select(d => new DocumentSummary
            {
                TenantId = d.TenantId,
                NaturalKey = d.NaturalKey,
                Type = d.Type,
                Status = d.Status,
                Attempts = d.Attempts,
                ExternalId = d.ExternalId,
                Reason = d.Reason,
                UpdatedAt = d.UpdatedAt,
            })
            .ToListAsync(ct);
}
