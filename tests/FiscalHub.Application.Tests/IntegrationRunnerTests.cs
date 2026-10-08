using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using FiscalHub.Application.Integrations;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o runner compartilhado: descobre, enfileira com o gatilho do modo e registra a execução. Manual fura a
/// idempotência; agendado dedupa por conteúdo. A descoberta é a do adapter de entrada do perfil do tenant, e o fallback de
/// desenvolvimento só responde quando ela falta (spec period-discovery, design D2 e D3).
/// </summary>
public class IntegrationRunnerTests
{
    [Fact]
    public async Task Manual_run_forces_reprocess_and_records_execution()
    {
        var discovery = new FakeDiscovery("Dynamics365", 2);
        var queue = new FakeQueue();
        var store = new FakeExecutionStore();
        var runner = new IntegrationRunner(Resolver("Dynamics365", [discovery]), queue, store, Clock);

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
        var discovery = new FakeDiscovery("Dynamics365", 1);
        var queue = new FakeQueue();
        var store = new FakeExecutionStore();
        var runner = new IntegrationRunner(Resolver("Dynamics365", [discovery]), queue, store, Clock);

        await runner.RunAsync(Request(IntegrationMode.ScheduledDaily));

        Assert.All(queue.Enqueued, r => Assert.Equal(IngestionTrigger.Event, r.Trigger)); // agendado dedupa
        Assert.Equal(IntegrationMode.ScheduledDaily, store.Recorded!.Mode);
    }

    [Fact]
    public async Task Each_reference_carries_the_instant_of_the_run_and_the_period_in_brasilia_days()
    {
        // O período como o agendador o monta: dia cheio em Brasília. Em UTC, o fim já seria o dia seguinte.
        var queue = new FakeQueue();
        var runner = new IntegrationRunner(Resolver("Dynamics365", [new FakeDiscovery("Dynamics365", 2)]), queue, new FakeExecutionStore(), Clock);

        await runner.RunAsync(Request(IntegrationMode.ScheduledOnce) with
        {
            PeriodStart = new DateTimeOffset(2016, 9, 1, 0, 0, 0, TimeSpan.FromHours(-3)),
            PeriodEnd = new DateTimeOffset(2016, 9, 30, 23, 59, 59, TimeSpan.FromHours(-3)),
        });

        Assert.Equal(2, queue.Enqueued.Count);
        Assert.All(queue.Enqueued, r =>
        {
            Assert.Equal(Clock.GetUtcNow(), r.ExecutedAt);
            Assert.Equal(new DateOnly(2016, 9, 1), r.PeriodStart);
            Assert.Equal(new DateOnly(2016, 9, 30), r.PeriodEnd);
            Assert.Equal("ScheduledOnce", r.SourceMode);
        });
    }

    // ---------- o estabelecimento que a descoberta resolveu (change explicit-credential-and-execution-cnpj, D5 e D6) ----------

    [Fact]
    public async Task The_execution_records_the_establishment_the_discovery_resolved()
    {
        var store = new FakeExecutionStore();
        var discovery = new FakeDiscovery("Dynamics365", 2) { EstablishmentTaxId = "44278225000260" };
        var runner = new IntegrationRunner(Resolver("Dynamics365", [discovery]), new FakeQueue(), store, Clock);

        await runner.RunAsync(Request(IntegrationMode.Manual) with { CompanyCode = "44278225000180", BranchCode = "SP-01" });

        Assert.Equal("44278225000180", store.Recorded!.CompanyCode);   // o critério continua como foi pedido
        Assert.Equal("SP-01", store.Recorded.BranchCode);
        Assert.Equal("44278225000260", store.Recorded.EstablishmentTaxId);   // e o fato, ao lado
    }

    [Fact]
    public async Task Without_a_resolved_establishment_the_execution_records_none()
    {
        var store = new FakeExecutionStore();
        var runner = new IntegrationRunner(Resolver("Dynamics365", [new FakeDiscovery("Dynamics365", 2)]), new FakeQueue(), store, Clock);

        await runner.RunAsync(Request(IntegrationMode.ScheduledDaily) with { CompanyCode = "44278225000180", BranchCode = null });

        Assert.Null(store.Recorded!.EstablishmentTaxId);
    }

    [Fact]
    public async Task A_branch_without_notes_still_records_its_establishment()
    {
        var store = new FakeExecutionStore();
        var queue = new FakeQueue();
        var discovery = new FakeDiscovery("Dynamics365", 0) { EstablishmentTaxId = "44278225000260" };
        var runner = new IntegrationRunner(Resolver("Dynamics365", [discovery]), queue, store, Clock);

        int count = await runner.RunAsync(Request(IntegrationMode.ScheduledOnce) with { CompanyCode = "44278225000180", BranchCode = "SP-01" });

        Assert.Equal(0, count);
        Assert.Empty(queue.Enqueued);
        Assert.Equal(0, store.Recorded!.DiscoveredCount);
        Assert.Equal("44278225000260", store.Recorded.EstablishmentTaxId);
    }

    // ---------- a escolha da descoberta ----------

    [Fact]
    public async Task Run_uses_the_discovery_of_the_inbound_adapter_and_never_the_fallback()
    {
        var d365 = new FakeDiscovery("Dynamics365", 1);
        var fallback = new FakeDiscovery("Local", 5);
        var runner = new IntegrationRunner(Resolver("Dynamics365", [d365], fallback), new FakeQueue(), new FakeExecutionStore(), Clock);

        int count = await runner.RunAsync(Request(IntegrationMode.Manual));

        Assert.Equal(1, count);
        Assert.Equal(1, d365.Calls);
        Assert.Equal(0, fallback.Calls);
        Assert.Equal("44278225000260", d365.Criteria!.Company);   // o critério chega como a tela o pediu
        Assert.Equal("SP-01", d365.Criteria.Establishment);
    }

    [Fact]
    public async Task Erp_without_discovery_gets_the_fallback_when_it_is_registered()
    {
        var fallback = new FakeDiscovery("Local", 2);
        var runner = new IntegrationRunner(Resolver("iScala", [new FakeDiscovery("Dynamics365", 1)], fallback), new FakeQueue(), new FakeExecutionStore(), Clock);

        Assert.Equal(2, await runner.RunAsync(Request(IntegrationMode.Manual)));
    }

    [Fact]
    public async Task Erp_without_discovery_and_without_fallback_fails_naming_the_adapter_and_the_tenant_with_nothing_queued()
    {
        var queue = new FakeQueue();
        var store = new FakeExecutionStore();
        var runner = new IntegrationRunner(Resolver("iScala", [new FakeDiscovery("Dynamics365", 1)]), queue, store, Clock);

        var ex = await Assert.ThrowsAsync<DocumentDiscoveryNotFoundException>(() => runner.RunAsync(Request(IntegrationMode.Manual)));

        Assert.Contains("iScala", ex.Message);
        Assert.Contains("tenant-a", ex.Message);
        Assert.Empty(queue.Enqueued);
        Assert.Null(store.Recorded);   // nenhuma execução registrada
    }

    [Fact]
    public async Task Tenant_without_profile_fails_naming_the_tenant()
    {
        var runner = new IntegrationRunner(
            new DocumentDiscoveryResolver([new FakeDiscovery("Dynamics365", 1)], new Profiles(null)), new FakeQueue(), new FakeExecutionStore(), Clock);

        var ex = await Assert.ThrowsAsync<DocumentDiscoveryNotFoundException>(() => runner.RunAsync(Request(IntegrationMode.Manual)));

        Assert.Contains("tenant-a", ex.Message);
        Assert.Contains("perfil", ex.Message);
    }

    // ---------- apoio ----------

    private static readonly TimeProvider Clock = new StubClock(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero));

    internal static DocumentDiscoveryResolver Resolver(string inboundAdapter, IEnumerable<IDocumentDiscovery> discoveries, IDocumentDiscovery? fallback = null)
        => new(discoveries, new Profiles(new TenantConnectorProfile
        {
            TenantId = "tenant-a",
            Environment = "Sandbox",
            InboundAdapter = inboundAdapter,
            OutboundAdapter = "Avalara",
        }), fallback);

    private static RunRequest Request(IntegrationMode mode) => new()
    {
        Mode = mode,
        TenantId = "tenant-a",
        CompanyCode = "44278225000260",
        BranchCode = "SP-01",
        PeriodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        PeriodEnd = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
    };

    internal sealed class FakeDiscovery(string origin, int count) : IDocumentDiscovery
    {
        public string Origin => origin;

        public int Calls { get; private set; }

        public DiscoveryCriteria? Criteria { get; private set; }

        /// <summary>As chaves que este fake acha no reprocesso; vazio = acha qualquer uma.</summary>
        public HashSet<string> Knows { get; init; } = [];

        public List<string> Asked { get; } = [];

        /// <summary>O CNPJ do estabelecimento que este fake diz ter resolvido; <c>null</c> = escopo de mais de um.</summary>
        public string? EstablishmentTaxId { get; init; }

        public Task<DiscoveryResult> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken ct = default)
        {
            Calls++;
            Criteria = criteria;
            IReadOnlyList<DocumentReference> refs = Enumerable.Range(1, count).Select(i => new DocumentReference
            {
                TenantId = criteria.TenantId,
                Type = DocumentType.GoodsInvoice55,
                NaturalKey = $"nfe-{i}",
                Locator = $"nfe/nfe-{i}.xml",
            }).ToList();
            return Task.FromResult(new DiscoveryResult(refs, EstablishmentTaxId));
        }

        public Task<DocumentReference?> FindByKeyAsync(string tenantId, string naturalKey, CancellationToken ct = default)
        {
            Asked.Add(naturalKey);
            return Task.FromResult<DocumentReference?>(Knows.Count > 0 && !Knows.Contains(naturalKey)
                ? null
                : new DocumentReference
                {
                    TenantId = tenantId,
                    Type = DocumentType.GoodsInvoice55,
                    NaturalKey = naturalKey,
                    Locator = $"{origin}/{naturalKey}",
                    Origin = origin,
                });
        }
    }

    internal sealed class FakeQueue : IDocumentQueue
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

    private sealed class StubClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Profiles(TenantConnectorProfile? profile) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(profile);

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantConnectorProfile>> ListByInboundAdapterAsync(string inboundAdapter, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectorProfile>>([]);
    }
}
