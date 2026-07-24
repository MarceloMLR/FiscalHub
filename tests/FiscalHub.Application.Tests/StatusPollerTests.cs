using FiscalHub.Application.Inbound;
using FiscalHub.Application.Metadata;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Pipeline;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Prova a lógica do poll sem Azure nem plataforma real: store e dispatcher são falsos. O poller
/// consulta cada documento em voo e grava o desfecho — confirma, erro, ou Unconfirmed no limite.
/// </summary>
public class StatusPollerTests
{
    private sealed record TestDocument(string Id);

    [Fact]
    public async Task Confirmed_result_marks_confirmed()
    {
        var store = new FakeStore(Pending("nfe-1", attempts: 0));
        var poller = new StatusPoller<TestDocument>(store, new FakeDispatcher(IntegrationStatus.Confirmed), new StatusPollerOptions());

        int count = await poller.PollOnceAsync();

        Assert.Equal(1, count);
        Assert.Equal(IntegrationStatus.Confirmed, store.LastMark!.Value.Status);
        Assert.Equal(1, store.LastMark.Value.Attempts);
    }

    [Fact]
    public async Task Error_result_marks_integration_error_with_message()
    {
        var store = new FakeStore(Pending("nfe-1", attempts: 0));
        var poller = new StatusPoller<TestDocument>(store, new FakeDispatcher(IntegrationStatus.IntegrationError, "rejeitada"), new StatusPollerOptions());

        await poller.PollOnceAsync();

        Assert.Equal(IntegrationStatus.IntegrationError, store.LastMark!.Value.Status);
        Assert.Equal("rejeitada", store.LastMark.Value.Reason);
    }

    [Fact]
    public async Task Still_pending_under_limit_stays_submitted_and_counts_the_attempt()
    {
        var store = new FakeStore(Pending("nfe-1", attempts: 1));
        var poller = new StatusPoller<TestDocument>(store, new FakeDispatcher(IntegrationStatus.Submitted), new StatusPollerOptions { MaxAttempts = 5 });

        await poller.PollOnceAsync();

        Assert.Equal(IntegrationStatus.Submitted, store.LastMark!.Value.Status);
        Assert.Equal(2, store.LastMark.Value.Attempts);
    }

    [Fact]
    public async Task Still_pending_at_limit_marks_unconfirmed()
    {
        var store = new FakeStore(Pending("nfe-1", attempts: 4));
        var poller = new StatusPoller<TestDocument>(store, new FakeDispatcher(IntegrationStatus.Submitted), new StatusPollerOptions { MaxAttempts = 5 });

        await poller.PollOnceAsync();

        Assert.Equal(IntegrationStatus.Unconfirmed, store.LastMark!.Value.Status);
        Assert.Equal(5, store.LastMark.Value.Attempts);
        Assert.NotNull(store.LastMark.Value.Reason);
    }

    [Fact]
    public async Task No_pending_documents_is_a_noop()
    {
        var store = new FakeStore();
        var poller = new StatusPoller<TestDocument>(store, new FakeDispatcher(IntegrationStatus.Confirmed), new StatusPollerOptions());

        Assert.Equal(0, await poller.PollOnceAsync());
        Assert.Null(store.LastMark);
    }

    [Fact]
    public async Task One_failing_document_does_not_block_the_rest_of_the_batch()
    {
        var store = new FakeStore(Pending("nfe-bad", attempts: 0), Pending("nfe-good", attempts: 0));
        var poller = new StatusPoller<TestDocument>(store, new SelectiveDispatcher("guid-nfe-bad"), new StatusPollerOptions());

        int count = await poller.PollOnceAsync();

        Assert.Equal(2, count);
        Assert.Contains(store.Marks, m => m.Key == "nfe-good" && m.Status == IntegrationStatus.Confirmed);
        Assert.DoesNotContain(store.Marks, m => m.Key == "nfe-bad");   // o que estourou nao foi marcado
    }

    private static PendingIntegration Pending(string key, int attempts) => new()
    {
        TenantId = "tenant-a",
        NaturalKey = key,
        ExternalId = "guid-" + key,
        Attempts = attempts,
    };

    private sealed class FakeDispatcher(IntegrationStatus status, string? message = null) : IComplianceDispatcher<TestDocument>
    {
        public string Destination => "fake";

        public Task<IntegrationReceipt> SubmitAsync(TestDocument document, DispatchContext context, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
            => Task.FromResult(new IntegrationResult { Status = status, Message = message });
    }

    private sealed class SelectiveDispatcher(string failingExternalId) : IComplianceDispatcher<TestDocument>
    {
        public string Destination => "fake";

        public Task<IntegrationReceipt> SubmitAsync(TestDocument document, DispatchContext context, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IntegrationResult> CheckStatusAsync(string externalId, DispatchContext context, CancellationToken ct = default)
            => externalId == failingExternalId
                ? throw new InvalidOperationException("falha simulada na consulta")
                : Task.FromResult(new IntegrationResult { Status = IntegrationStatus.Confirmed });
    }

    private sealed class FakeStore : IProcessingStore
    {
        private readonly List<PendingIntegration> _pending;

        public FakeStore(params PendingIntegration[] pending) => _pending = [.. pending];

        public (IntegrationStatus Status, string? Reason, int Attempts)? LastMark { get; private set; }

        public List<(string Key, IntegrationStatus Status, string? Reason, int Attempts)> Marks { get; } = [];

        public Task<IReadOnlyList<PendingIntegration>> ListPendingAsync(int batchSize, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PendingIntegration>>(_pending);

        public Task MarkPolledAsync(string tenantId, string naturalKey, IntegrationStatus status, string? reason, int attempts, CancellationToken ct = default)
        {
            LastMark = (status, reason, attempts);
            Marks.Add((naturalKey, status, reason, attempts));
            return Task.CompletedTask;
        }

        public Task RecordDeadLetterAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordMetadataAsync(DocumentReference reference, DocumentMetadata metadata, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> AlreadySubmittedAsync(string tenantId, string naturalKey, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task RecordSubmissionAsync(DocumentReference reference, IntegrationReceipt receipt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordRejectionAsync(DocumentReference reference, string reason, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
