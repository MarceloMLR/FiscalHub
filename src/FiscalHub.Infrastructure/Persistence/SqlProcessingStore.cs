using FiscalHub.Application.Inbound;
using FiscalHub.Application.Integrations;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Persistence;

/// <summary>Implementação de <see cref="IProcessingStore"/> em banco relacional (EF Core / Azure SQL).</summary>
internal sealed class SqlProcessingStore : IProcessingStore
{
    /// <summary>O modo da nota que entrou sem ação humana (coletor, drop, evento): a referência sem <c>SourceMode</c>.</summary>
    internal const string AutomaticMode = "Automatic";

    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _clock;

    public SqlProcessingStore(ProcessingDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<bool> AlreadyProcessedAsync(string tenantId, string naturalKey, string contentHash, CancellationToken ct = default)
        => _db.ProcessedDocuments.AnyAsync(
            d => d.TenantId == tenantId
                 && d.NaturalKey == naturalKey
                 && d.ContentHash == contentHash   // mesmo cru; se mudou (correção), não bloqueia
                 && (d.Status == IntegrationStatus.Submitted || d.Status == IntegrationStatus.Confirmed),
            ct);

    // O que o destino não levou fica visível como observação do envio (ADR-0026); sem omissão, o Reason fica vazio.
    public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
        => UpsertAsync(
            reference, receipt.Status, receipt.ExternalId,
            reason: receipt.Omissions.Count == 0 ? null : "Enviado sem: " + string.Join("; ", receipt.Omissions),
            ct);

    public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        => UpsertAsync(reference, IntegrationStatus.IntegrationError, externalId: null, reason, ct);

    public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        => UpsertAsync(reference, IntegrationStatus.DeadLettered, externalId: null, reason, ct);

    public Task RecordIgnoredAsync(DocumentReference reference, string reason, CancellationToken ct = default)
        => UpsertAsync(reference, IntegrationStatus.Ignored, externalId: null, reason, ct);   // o upsert preserva o ExternalId

    public async Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, string contentHash, CancellationToken ct = default)
    {
        ProcessedDocument? row = await _db.ProcessedDocuments.FirstOrDefaultAsync(
            d => d.TenantId == reference.TenantId && d.NaturalKey == reference.NaturalKey, ct);

        DateTimeOffset now = _clock.GetUtcNow();
        string refDate = metadata.ReferenceDate.ToString("yyyy-MM-dd");

        if (row is null)
        {
            row = new ProcessedDocument
            {
                TenantId = reference.TenantId,
                NaturalKey = reference.NaturalKey,
                Type = reference.Type,
                Status = IntegrationStatus.Pending,
                CompanyCode = metadata.CompanyCode,
                BranchCode = metadata.BranchCode,
                ReferenceDate = refDate,
                DocumentNumber = metadata.DocumentNumber,
                DocumentModel = metadata.DocumentModel,
                Trigger = reference.SourceMode ?? AutomaticMode,   // sem modo = entrou sem ação humana (coletor, drop, evento)
                ContentHash = contentHash,
                CreatedAt = now,
                UpdatedAt = now,
            };
            ApplyExecution(row, reference, isNew: true, now);
            _db.ProcessedDocuments.Add(row);
        }
        else
        {
            row.CompanyCode = metadata.CompanyCode;
            row.BranchCode = metadata.BranchCode;
            row.ReferenceDate = refDate;
            row.DocumentNumber = metadata.DocumentNumber;
            row.DocumentModel = metadata.DocumentModel;
            if (reference.SourceMode is not null)
            {
                row.Trigger = reference.SourceMode;   // só troca o rótulo se veio um modo explícito (não em reprocesso por evento)
            }
            row.ContentHash = contentHash;   // correção reintegrando: grava o hash do cru novo
            row.UpdatedAt = now;
            ApplyExecution(row, reference, isNew: false, now);
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

        // Enquanto aceita, o Reason só pode ser a ressalva do envio (as omissões). Confirmação ou "ainda processando" a
        // preservam. Numa falha (recusa, sem retorno), o motivo é só o da falha: a ressalva é de nota aceita, e a omissão
        // continua na foto do envio (establishment-and-readable-dashboard, D9).
        string? submissionNote = row.Status == IntegrationStatus.Submitted ? row.Reason : null;
        row.Status = status;
        row.Reason = status is IntegrationStatus.IntegrationError or IntegrationStatus.Unconfirmed ? reason : reason ?? submissionNote;
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
            row = new ProcessedDocument
            {
                TenantId = reference.TenantId,
                NaturalKey = reference.NaturalKey,
                Type = reference.Type,
                Status = status,
                ExternalId = externalId,
                Reason = reason,
                Trigger = reference.SourceMode ?? AutomaticMode,   // o modo em qualquer desfecho, também o que não chega à montagem
                CreatedAt = now,
                UpdatedAt = now,
            };
            ApplyExecution(row, reference, isNew: true, now);
            _db.ProcessedDocuments.Add(row);
        }
        else
        {
            row.Status = status;
            row.ExternalId = externalId ?? row.ExternalId;
            row.Reason = reason;
            row.Attempts = 0;   // (re)submissão reinicia a contagem de consultas
            row.UpdatedAt = now;
            ApplyExecution(row, reference, isNew: false, now);   // a ignorada de outra execução também muda de linha
        }

        // O grupo visto na descoberta, para a nota que não chega à montagem (design D4). O da montagem, já gravado pelo
        // RecordMetadataAsync, nunca é trocado por ele.
        if (row.CompanyCode is null && reference.Metadata is { } discovered)
        {
            ApplyGroup(row, discovered);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A execução que trouxe a nota (change erp-company-directory-and-card-filters, D15). A referência com o instante da
    /// execução move a nota para ela, que é a última entrada: o dia, o modo e o período passam a ser os dela. Sem o instante
    /// (o reprocesso, o drop, o /ingest, a mensagem antiga), a linha que já existe não muda, e a nova nasce no dia do
    /// processamento, como automática e sem período.
    /// </summary>
    private static void ApplyExecution(ProcessedDocument row, DocumentReference reference, bool isNew, DateTimeOffset now)
    {
        if (reference.ExecutedAt is { } executedAt)
        {
            row.ExecutedOn = Day(ExecutionDay.Of(executedAt));
            row.PeriodStart = reference.PeriodStart is { } start ? Day(start) : null;
            row.PeriodEnd = reference.PeriodEnd is { } end ? Day(end) : null;
            row.Trigger = reference.SourceMode ?? AutomaticMode;
        }
        else if (isNew)
        {
            row.ExecutedOn = Day(ExecutionDay.Of(now));
        }
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd");

    private static void ApplyGroup(ProcessedDocument row, DocumentMetadata metadata)
    {
        row.CompanyCode = metadata.CompanyCode;
        row.BranchCode = metadata.BranchCode;
        row.ReferenceDate = metadata.ReferenceDate.ToString("yyyy-MM-dd");
        row.DocumentNumber = metadata.DocumentNumber;
        row.DocumentModel = metadata.DocumentModel;
    }
}
