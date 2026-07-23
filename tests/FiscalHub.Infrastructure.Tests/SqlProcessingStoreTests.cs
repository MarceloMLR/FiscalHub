using FiscalHub.Application.Inbound;
using FiscalHub.Application.Outbound;
using FiscalHub.Domain.Envelope;
using FiscalHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiscalHub.Infrastructure.Tests;

/// <summary>
/// Especifica o store de rastreio/idempotência. Roda em SQLite in-memory, que respeita o índice
/// único de verdade (produção usa SQL Server). Sem libs de mock.
/// </summary>
public class SqlProcessingStoreTests
{
    [Fact]
    public async Task Unknown_document_is_not_submitted()
    {
        using var h = NewStore();

        Assert.False(await h.Store.AlreadySubmittedAsync("tenant-a", "nfe-1"));
    }

    [Fact]
    public async Task Recorded_submission_is_marked_submitted()
    {
        using var h = NewStore();

        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        Assert.True(await h.Store.AlreadySubmittedAsync("tenant-a", "nfe-1"));
    }

    [Fact]
    public async Task Rejected_document_is_not_marked_submitted()
    {
        using var h = NewStore();

        await h.Store.RecordRejectionAsync(Reference("nfe-1"), "Item 1: CFOP invalido");

        Assert.False(await h.Store.AlreadySubmittedAsync("tenant-a", "nfe-1"));
    }

    [Fact]
    public async Task Recording_the_same_document_twice_upserts_one_row()
    {
        using var h = NewStore();

        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());   // reentrega

        Assert.Equal(1, await h.Db.ProcessedDocuments.CountAsync());
    }

    [Fact]
    public async Task Unique_index_blocks_duplicate_natural_key()
    {
        using var h = NewStore();

        h.Db.ProcessedDocuments.Add(Row("nfe-1"));
        h.Db.ProcessedDocuments.Add(Row("nfe-1"));   // mesma (tenant, chave) — o banco recusa

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Lists_submitted_documents_with_external_id_as_pending()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        var pending = await h.Store.ListPendingAsync(50);

        Assert.Single(pending);
        Assert.Equal("nfe-1", pending[0].NaturalKey);
        Assert.Equal("guid-1", pending[0].ExternalId);
    }

    [Fact]
    public async Task Confirmed_document_leaves_the_poll_queue_but_still_blocks_resubmission()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Confirmed, null, 1);

        Assert.Empty(await h.Store.ListPendingAsync(50));                     // saiu da fila de poll
        Assert.True(await h.Store.AlreadySubmittedAsync("tenant-a", "nfe-1")); // confirmado ainda bloqueia reenvio
    }

    [Fact]
    public async Task Errored_document_is_reopened_for_resubmission()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.IntegrationError, "campo X ausente", 1);

        Assert.False(await h.Store.AlreadySubmittedAsync("tenant-a", "nfe-1")); // erro libera reenvio
    }

    [Fact]
    public async Task Resubmission_resets_poll_attempts()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());
        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Submitted, null, 3);

        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());   // reenvio

        var pending = await h.Store.ListPendingAsync(50);
        Assert.Equal(0, pending[0].Attempts);
    }

    [Fact]
    public async Task Dead_lettered_document_is_recorded_and_reopens_for_resubmission()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.RecordDeadLetterAsync(Reference("nfe-1"), "MaxDeliveryCountExceeded");

        Assert.False(await h.Store.AlreadySubmittedAsync("tenant-a", "nfe-1")); // dead-letter libera reenvio
        Assert.Empty(await h.Store.ListPendingAsync(50));                        // saiu da fila de poll
    }

    private static Harness NewStore()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ProcessingDbContext>().UseSqlite(conn).Options;
        var db = new ProcessingDbContext(options);
        db.Database.EnsureCreated();
        return new Harness(db, conn, new SqlProcessingStore(db, TimeProvider.System));
    }

    private static DocumentReference Reference(string key) => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = key,
        Locator = "blob://" + key,
    };

    private static IntegrationReceipt Receipt() => new()
    {
        ExternalId = "guid-1",
        Status = IntegrationStatus.Submitted,
    };

    private static ProcessedDocument Row(string key) => new()
    {
        TenantId = "tenant-a",
        NaturalKey = key,
        Type = DocumentType.GoodsInvoice55,
        Status = IntegrationStatus.Submitted,
    };

    private sealed class Harness(ProcessingDbContext db, SqliteConnection conn, SqlProcessingStore store) : IDisposable
    {
        public ProcessingDbContext Db => db;

        public SqlProcessingStore Store => store;

        public void Dispose()
        {
            db.Dispose();
            conn.Dispose();
        }
    }
}
