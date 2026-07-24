using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
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

    public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        => UpsertAsync(reference, IntegrationStatus.DeadLettered, externalId: null, reason, ct);

    public async Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, CancellationToken ct = default)
    {
        ProcessedDocument? row = await _db.ProcessedDocuments.FirstOrDefaultAsync(
            d => d.TenantId == reference.TenantId && d.NaturalKey == reference.NaturalKey, ct);

        DateTimeOffset now = _clock.GetUtcNow();
        string refDate = metadata.ReferenceDate.ToString("yyyy-MM-dd");

        if (row is null)
        {
            _db.ProcessedDocuments.Add(new ProcessedDocument
            {
                TenantId = reference.TenantId,
                NaturalKey = reference.NaturalKey,
                Type = reference.Type,
                Status = IntegrationStatus.Pending,
                CompanyCode = metadata.CompanyCode,
                BranchCode = metadata.BranchCode,
                ReferenceDate = refDate,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            row.CompanyCode = metadata.CompanyCode;
            row.BranchCode = metadata.BranchCode;
            row.ReferenceDate = refDate;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default)
        => await _db.ProcessedDocuments
            .Where(d => d.Status == IntegrationStatus.Submitted && d.ExternalId != null)
            .OrderBy(d => d.Id)   // ordem de inserção (FIFO); Id ordena nos dois providers, DateTimeOffset não no SQLite
            .Take(batchSize)
            .Select(d => new PendingIntegration
            {
                TenantId = d.TenantId,
                NaturalKey = d.NaturalKey,
                ExternalId = d.ExternalId!,
                Attempts = d.Attempts,
            })
            .ToListAsync(ct);

    public async Task MarkPolledAsync(
        string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default)
    {
        ProcessedDocument? row = await _db.ProcessedDocuments.FirstOrDefaultAsync(
            d => d.TenantId == tenantId && d.NaturalKey == naturalKey, ct);

        if (row is null)
        {
            return;
        }

        row.Status = status;
        row.Reason = reason;
        row.Attempts = attempts;
        row.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
    }

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
            row.Attempts = 0;   // (re)submissão reinicia a contagem de consultas
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }
}
