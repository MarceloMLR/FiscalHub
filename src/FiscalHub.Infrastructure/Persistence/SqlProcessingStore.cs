using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Implementação de <see cref="IProcessingStore"/> em banco relacional (EF Core / Azure SQL).</summary>
internal sealed class SqlProcessingStore : IProcessingStore
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlProcessingStore(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<bool> AlreadySubmittedAsync(string tenantId, string naturalKey, CancellationToken ct = default)
        => _db.ProcessedDocuments.AnyAsync(
            d => d.TenantId == tenantId
                 && d.NaturalKey == naturalKey
                 && (d.Status == IntegrationStatus.Submitted || d.Status == IntegrationStatus.Confirmed),
            ct);

    public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        => UpsertAsync(reference, receipt.Status, receipt.ExternalId, reason: null, ct);

    public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        => UpsertAsync(reference, IntegrationStatus.IntegrationError, externalId: null, reason, ct);

    private async Task UpsertAsync(
        DocumentReference reference, IntegrationStatus status, string? externalId, string? reason, CancellationToken ct)
    {
        ProcessedDocument? row = await _db.ProcessedDocuments.FirstOrDefaultAsync(
            d => d.TenantId == reference.TenantId && d.NaturalKey == reference.NaturalKey, ct);

        DateTimeOffset now = _clock.GetUtcNow();

        if (row is null)
        {
            _db.ProcessedDocuments.Add(new ProcessedDocument
            {
                TenantId = reference.TenantId,
                NaturalKey = reference.NaturalKey,
                Type = reference.Type,
                Status = status,
                ExternalId = externalId,
                Reason = reason,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            row.Status = status;
            row.ExternalId = externalId ?? row.ExternalId;
            row.Reason = reason;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }
}
