using FiscalHub.Application.Inbound;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>A contagem de reprocessos no registro do documento: um UPDATE só, que soma um, sem ler a linha antes.</summary>
internal sealed class SqlReprocessLog : IReprocessLog
{
    private readonly ProcessingDbContext _db;

    public SqlReprocessLog(ProcessingDbContext db) => _db = db;

    public Task RecordAsync(string tenantId, string naturalKey, CancellationToken ct = default)
        => _db.ProcessedDocuments
            .Where(d => d.TenantId == tenantId && d.NaturalKey == naturalKey)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ReprocessCount, d => d.ReprocessCount + 1), ct);
}
