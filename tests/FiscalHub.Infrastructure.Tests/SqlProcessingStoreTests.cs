using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
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

        Assert.False(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash));
    }

    [Fact]
    public async Task Recorded_submission_is_marked_submitted()
    {
        using var h = NewStore();

        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        Assert.True(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash));
    }

    [Fact]
    public async Task Rejected_document_is_not_marked_submitted()
    {
        using var h = NewStore();

        await h.Store.RecordRejectionAsync(Reference("nfe-1"), "Item 1: CFOP invalido");

        Assert.False(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash));
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
        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Confirmed, null, 1);

        Assert.Empty(await h.Store.ListPendingAsync(50));                              // saiu da fila de poll
        Assert.True(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash));   // mesmo cru confirmado ainda bloqueia
    }

    [Fact]
    public async Task Errored_document_is_reopened_for_resubmission()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.IntegrationError, "campo X ausente", 1);

        Assert.False(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash)); // erro libera reenvio
    }

    [Fact]
    public async Task Same_key_with_corrected_content_is_not_blocked()
    {
        using var h = NewStore();
        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());
        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Confirmed, null, 1);

        // Mesma chave, mesmo cru (duplicata de evento): bloqueia. Cru corrigido (hash novo): reintegra.
        Assert.True(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash));
        Assert.False(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", "hash-corrigido"));
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
    public async Task Queries_group_by_company_branch_and_day()
    {
        using var h = NewStore();
        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());
        await h.Store.RecordMetadataAsync(Reference("nfe-2"), Meta(), Hash);
        await h.Store.RecordSubmissionAsync(Reference("nfe-2"), Receipt());
        await h.Store.MarkPolledAsync("tenant-a", "nfe-2", IntegrationStatus.Confirmed, null, 1);

        var queries = new SqlDocumentQueries(h.Db, new StubTenantContext("tenant-a"));
        var groups = await queries.ListGroupsAsync(50);

        Assert.Single(groups);                       // mesma empresa/filial/dia
        Assert.Equal(2, groups[0].Total);
        Assert.Equal(1, groups[0].Finalizadas);      // nfe-2 confirmada
        Assert.Equal(1, groups[0].EmProcessamento);  // nfe-1 ainda submitted
        Assert.Equal("12345678", groups[0].CompanyCode);
    }

    [Fact]
    public async Task Queries_list_recent_returns_documents_newest_first()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());
        await h.Store.RecordSubmissionAsync(Reference("nfe-2"), Receipt());

        var queries = new SqlDocumentQueries(h.Db, new StubTenantContext("tenant-a"));
        var list = await queries.ListRecentAsync(10);

        Assert.Equal(2, list.Count);
        Assert.Equal("nfe-2", list[0].NaturalKey);   // mais novo primeiro
        Assert.Equal("nfe-1", list[1].NaturalKey);
    }

    [Fact]
    public async Task Dead_lettered_document_is_recorded_and_reopens_for_resubmission()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.RecordDeadLetterAsync(Reference("nfe-1"), "MaxDeliveryCountExceeded");

        Assert.False(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash)); // dead-letter libera reenvio
        Assert.Empty(await h.Store.ListPendingAsync(50));                        // saiu da fila de poll
    }

    [Fact]
    public async Task Ignored_document_is_recorded_with_its_reason()
    {
        using var h = NewStore();

        await h.Store.RecordIgnoredAsync(Reference("brmf|SE-1") with { Type = DocumentType.ServiceNfse }, "ignorado: tipo fora do escopo (ServiceNfse)");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.Ignored, row.Status);
        Assert.Equal("ignorado: tipo fora do escopo (ServiceNfse)", row.Reason);
        Assert.Equal(DocumentType.ServiceNfse, row.Type);
    }

    [Fact]
    public async Task Ignoring_a_confirmed_document_keeps_its_external_id()
    {
        using var h = NewStore();
        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());
        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Confirmed, null, 1);

        // Nota enviada e depois cancelada no F&O (ADR-0025 §4): passa a ignorada, sem perder o GUID do destino.
        await h.Store.RecordIgnoredAsync(Reference("nfe-1"), "ignorado: status Cancelled fora do escopo");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.Ignored, row.Status);
        Assert.Equal("guid-1", row.ExternalId);
        Assert.Equal("ignorado: status Cancelled fora do escopo", row.Reason);
    }

    [Fact]
    public async Task Ignoring_the_same_document_again_keeps_one_row()
    {
        using var h = NewStore();

        await h.Store.RecordIgnoredAsync(Reference("brmf|SE-1"), "ignorado: tipo fora do escopo (ServiceNfse)");
        await h.Store.RecordIgnoredAsync(Reference("brmf|SE-1"), "ignorado: tipo fora do escopo (ServiceNfse)");

        Assert.Equal(1, await h.Db.ProcessedDocuments.CountAsync());
    }

    [Fact]
    public async Task Ignored_document_does_not_count_as_already_processed()
    {
        using var h = NewStore();
        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);
        await h.Store.RecordIgnoredAsync(Reference("nfe-1"), "ignorado: status Cancelled fora do escopo");

        Assert.False(await h.Store.AlreadyProcessedAsync("tenant-a", "nfe-1", Hash));
    }

    // ---------- o grupo da descoberta na nota que não chega à montagem (establishment-and-readable-dashboard, D4) ----------

    [Fact]
    public async Task Ignored_note_is_recorded_with_the_discovered_group_and_mode()
    {
        using var h = NewStore();

        await h.Store.RecordIgnoredAsync(Service("brmf|SE-1") with { Metadata = Discovered() }, "ignorado: tipo fora do escopo (ServiceNfse)");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.Ignored, row.Status);
        Assert.Equal("44278225000260", row.CompanyCode);
        Assert.Equal("SP-01", row.BranchCode);
        Assert.Equal("2026-08-07", row.ReferenceDate);
        Assert.Equal("000123", row.DocumentNumber);
        Assert.Equal("SE", row.DocumentModel);
        Assert.Equal("Automatic", row.Trigger);   // sem modo = entrou sem ação humana
    }

    [Fact]
    public async Task Ignored_note_of_a_manual_run_keeps_the_manual_mode()
    {
        using var h = NewStore();

        await h.Store.RecordIgnoredAsync(Service("brmf|SE-1") with { Metadata = Discovered(), SourceMode = "Manual" }, "ignorado");

        Assert.Equal("Manual", (await h.Db.ProcessedDocuments.SingleAsync()).Trigger);
    }

    [Fact]
    public async Task Assembled_group_is_kept_when_a_later_outcome_brings_the_discovered_one()
    {
        using var h = NewStore();
        await h.Store.RecordMetadataAsync(Reference("nfe-1"), Meta(), Hash);

        await h.Store.RecordRejectionAsync(Reference("nfe-1") with { Metadata = Discovered() }, "Plataforma de compliance recusou: X");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal("12345678", row.CompanyCode);      // o da montagem, e não o da descoberta
        Assert.Equal("2026-07-23", row.ReferenceDate);
        Assert.Equal("55", row.DocumentModel);
    }

    [Fact]
    public async Task Existing_row_without_group_receives_the_discovered_one()
    {
        using var h = NewStore();
        await h.Store.RecordIgnoredAsync(Service("brmf|SE-1"), "ignorado");   // passada antiga, sem grupo

        await h.Store.RecordIgnoredAsync(Service("brmf|SE-1") with { Metadata = Discovered() }, "ignorado");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal("44278225000260", row.CompanyCode);
        Assert.Equal("2026-08-07", row.ReferenceDate);
    }

    [Fact]
    public async Task Reference_without_group_records_the_outcome_without_it()
    {
        using var h = NewStore();

        await h.Store.RecordIgnoredAsync(Service("brmf|SE-1"), "ignorado: tipo fora do escopo (ServiceNfse)");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.Ignored, row.Status);
        Assert.Null(row.CompanyCode);
        Assert.Null(row.ReferenceDate);
    }

    [Fact]
    public async Task Dead_letter_is_recorded_with_the_discovered_group()
    {
        using var h = NewStore();

        await h.Store.RecordDeadLetterAsync(Reference("brmf|BRMF-55") with { Metadata = Discovered() with { DocumentModel = "55" } }, "MaxDeliveryCountExceeded");

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.DeadLettered, row.Status);
        Assert.Equal("44278225000260", row.CompanyCode);
        Assert.Equal("2026-08-07", row.ReferenceDate);
    }

    [Fact]
    public async Task Ignored_note_counts_in_the_day_total_and_not_as_error()
    {
        using var h = NewStore();
        var sameDay = Discovered() with { DocumentModel = "55" };
        await h.Store.RecordMetadataAsync(Reference("brmf|NFE-1"), sameDay, Hash);
        await h.Store.RecordSubmissionAsync(Reference("brmf|NFE-1"), Receipt());
        await h.Store.MarkPolledAsync("tenant-a", "brmf|NFE-1", IntegrationStatus.Confirmed, null, 1);
        await h.Store.RecordIgnoredAsync(Service("brmf|SE-1") with { Metadata = Discovered() }, "ignorado: tipo fora do escopo (ServiceNfse)");

        var queries = new SqlDocumentQueries(h.Db, new StubTenantContext("tenant-a"));
        IReadOnlyList<Application.Queries.DocumentGroup> groups = await queries.ListGroupsAsync(50);

        // Um grupo por tipo no mesmo dia do mesmo estabelecimento; somados, são o card "Documentos" do dia.
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal("2026-08-07", g.ReferenceDate));
        Assert.Equal(2, groups.Sum(g => g.Total));
        Assert.Equal(1, groups.Sum(g => g.Finalizadas));
        Assert.Equal(0, groups.Sum(g => g.ComErro));
        Assert.Equal(0, groups.Sum(g => g.EmProcessamento));
        Application.Queries.DocumentGroup ignored = Assert.Single(groups, g => g.Type == DocumentType.ServiceNfse);
        Assert.Equal(1, ignored.Total);
        Assert.Equal("Automatic", ignored.Trigger);
    }

    // ---------- omissões do envio: observação visível no Reason (ADR-0026, design D11) ----------

    [Fact]
    public async Task Submission_without_omissions_leaves_the_reason_empty()
    {
        using var h = NewStore();

        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        Assert.Null((await h.Db.ProcessedDocuments.SingleAsync()).Reason);
    }

    [Fact]
    public async Task Submission_with_omissions_records_them_as_the_reason()
    {
        using var h = NewStore();

        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt() with { Omissions = ["item 1: a", "item 2: b"] });

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.Submitted, row.Status);
        Assert.Equal("Enviado sem: item 1: a; item 2: b", row.Reason);
    }

    [Fact]
    public async Task Confirmation_keeps_the_omissions()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt() with { Omissions = ["item 1: a"] });

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Confirmed, null, 1);

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.Confirmed, row.Status);
        Assert.Equal("Enviado sem: item 1: a", row.Reason);
    }

    [Fact]
    public async Task Still_processing_keeps_the_omissions()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt() with { Omissions = ["item 1: a"] });

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.Submitted, null, 1);

        Assert.Equal("Enviado sem: item 1: a", (await h.Db.ProcessedDocuments.SingleAsync()).Reason);
    }

    [Fact]
    public async Task Platform_rejection_comes_first_and_keeps_the_omissions_after_it()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt() with { Omissions = ["item 1: a"] });

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.IntegrationError, "Plataforma de compliance rejeitou: X", 1);

        ProcessedDocument row = await h.Db.ProcessedDocuments.SingleAsync();
        Assert.Equal(IntegrationStatus.IntegrationError, row.Status);
        Assert.Equal("Plataforma de compliance rejeitou: X | Enviado sem: item 1: a", row.Reason);
    }

    [Fact]
    public async Task Platform_rejection_without_previous_omissions_is_just_the_reason()
    {
        using var h = NewStore();
        await h.Store.RecordSubmissionAsync(Reference("nfe-1"), Receipt());

        await h.Store.MarkPolledAsync("tenant-a", "nfe-1", IntegrationStatus.IntegrationError, "Plataforma de compliance rejeitou: X", 1);

        Assert.Equal("Plataforma de compliance rejeitou: X", (await h.Db.ProcessedDocuments.SingleAsync()).Reason);
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

    private const string Hash = "sha256-do-cru-1";

    private static DocumentReference Reference(string key) => new()
    {
        TenantId = "tenant-a",
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = key,
        Locator = "blob://" + key,
    };

    private static DocumentReference Service(string key) => Reference(key) with { Type = DocumentType.ServiceNfse };

    /// <summary>O grupo que a descoberta do D365 leu de uma NFS-e da <c>SP-01</c>.</summary>
    private static DocumentMetadata Discovered() => new()
    {
        CompanyCode = "44278225000260",
        BranchCode = "SP-01",
        ReferenceDate = new DateOnly(2026, 8, 7),
        DocumentNumber = "000123",
        DocumentModel = "SE",
    };

    private static IntegrationReceipt Receipt() => new()
    {
        ExternalId = "guid-1",
        Status = IntegrationStatus.Submitted,
    };

    private static DocumentMetadata Meta() => new()
    {
        CompanyCode = "12345678",
        BranchCode = "0001",
        ReferenceDate = new DateOnly(2026, 7, 23),
        DocumentNumber = "123",
        DocumentModel = "55",
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
