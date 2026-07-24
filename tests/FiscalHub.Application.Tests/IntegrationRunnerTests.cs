using FiscalHub.Application.Inbound;
using FiscalHub.Application.Integrations;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o runner compartilhado: descobre, enfileira com o gatilho do modo e registra a
/// execução. Manual fura a idempotência; agendado dedupa por conteúdo.
/// </summary>
public class IntegrationRunnerTests
{
    [Fact]
    public async Task Manual_run_forces_reprocess_and_records_execution()
    {
        var discovery = new FakeDiscovery(2);
        var queue = new FakeQueue();
        var store = new FakeExecutionStore();
        var runner = new IntegrationRunner(discovery, queue, store);

        int count = await runner.RunAsync(Request(IntegrationMode.Manual));

        Assert.Equal(2, count);
        Assert.Equal(2, queue.Enqueued.Count);
        Assert.All(queue.Enqueued, r => Assert.Equal(IngestionTrigger.Manual, r.Trigger)); // manual fura
        Assert.Equal(2, store.Recorded!.DiscoveredCount);
        Assert.Equal(IntegrationMode.Manual, store.Recorded.Mode);
    }

    [Fact]
    public async Task Scheduled_run_dedupes_by_content()
    {
        var discovery = new FakeDiscovery(1);
        var queue = new FakeQueue();
        var store = new FakeExecutionStore();
        var runner = new IntegrationRunner(discovery, queue, store);

        await runner.RunAsync(Request(IntegrationMode.ScheduledDaily));

        Assert.All(queue.Enqueued, r => Assert.Equal(IngestionTrigger.Event, r.Trigger)); // agendado dedupa
        Assert.Equal(IntegrationMode.ScheduledDaily, store.Recorded!.Mode);
    }

    private static RunRequest Request(IntegrationMode mode) => new()
    {
        Mode = mode,
        TenantId = "tenant-a",
        CompanyCode = "12345678",
        BranchCode = null,
        PeriodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        PeriodEnd = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeDiscovery(int count) : IDocumentDiscovery
    {
        public string Origin => "fake";

        public Task<IReadOnlyList<DocumentReference>> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken ct = default)
        {
            IReadOnlyList<DocumentReference> refs = Enumerable.Range(1, count).Select(i => new DocumentReference
            {
                TenantId = criteria.TenantId,
                Type = DocumentType.GoodsInvoice55,
                NaturalKey = $"nfe-{i}",
                Locator = $"nfe/nfe-{i}.xml",
            }).ToList();
            return Task.FromResult(refs);
        }
    }

    private sealed class FakeQueue : IDocumentQueue
    {
        public List<DocumentReference> Enqueued { get; } = [];

        public Task EnqueueAsync(DocumentReference reference, CancellationToken ct = default)
        {
            Enqueued.Add(reference);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExecutionStore : IExecutionStore
    {
        public IntegrationExecution? Recorded { get; private set; }

        public Task RecordAsync(IntegrationExecution execution, CancellationToken ct = default)
        {
            Recorded = execution;
            return Task.CompletedTask;
        }
    }
}
