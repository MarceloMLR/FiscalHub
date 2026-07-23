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
}
