using FiscalHub.Application.Auth;
using FiscalHub.Application.Inbound;
using static FiscalHub.Application.Tests.IntegrationRunnerTests;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica o reprocesso (spec period-discovery, "O reprocesso acha a nota na origem dela", design D5): a nota é procurada
/// primeiro na descoberta do adapter de entrada do perfil e, em Development, no catálogo local; vale a primeira que acha. A
/// referência volta com o gatilho manual, que fura a idempotência. A nota de outro tenant tem a resposta de "não encontrada",
/// sem perguntar a ninguém. O reprocesso aceito é contado, e só ele (conferência na tela, 2026-10-02).
/// </summary>
public class DocumentReprocessTests
{
    private const string D365Key = "brmf|BRMF06-110000027";
    private const string XmlKey = "35260612345678000190550010000001231000000123";

    [Fact]
    public async Task Note_of_the_d365_is_found_by_the_d365_and_requeued_as_manual()
    {
        var d365 = new FakeDiscovery("Dynamics365", 0) { Knows = [D365Key] };
        var fallback = new FakeDiscovery("Local", 0) { Knows = [XmlKey] };
        var queue = new FakeQueue();

        ReprocessStatus status = await Reprocess(d365, fallback, queue).ReprocessAsync("tenant-a", D365Key);

        Assert.Equal(ReprocessStatus.Queued, status);
        DocumentReference queued = Assert.Single(queue.Enqueued);
        Assert.Equal(IngestionTrigger.Manual, queued.Trigger);
        Assert.Equal("Dynamics365", queued.Origin);
        Assert.Empty(fallback.Asked);   // achou na primeira: o catálogo nem é perguntado
        Assert.Equal([("tenant-a", D365Key)], Log.Recorded);   // contado uma vez
    }

    [Fact]
    public async Task Sample_note_falls_to_the_local_catalog_after_the_d365()
    {
        var d365 = new FakeDiscovery("Dynamics365", 0) { Knows = [D365Key] };
        var fallback = new FakeDiscovery("Local", 0) { Knows = [XmlKey] };
        var queue = new FakeQueue();

        ReprocessStatus status = await Reprocess(d365, fallback, queue).ReprocessAsync("tenant-a", XmlKey);

        Assert.Equal(ReprocessStatus.Queued, status);
        Assert.Equal([XmlKey], d365.Asked);        // a ordem: a do adapter primeiro
        Assert.Equal([XmlKey], fallback.Asked);
        Assert.Equal("Local", Assert.Single(queue.Enqueued).Origin);
        Assert.Equal(IngestionTrigger.Manual, queue.Enqueued[0].Trigger);
    }

    [Fact]
    public async Task Note_that_no_origin_has_is_not_found_and_nothing_is_queued()
    {
        var queue = new FakeQueue();

        ReprocessStatus status = await Reprocess(
            new FakeDiscovery("Dynamics365", 0) { Knows = [D365Key] }, new FakeDiscovery("Local", 0) { Knows = [XmlKey] }, queue)
            .ReprocessAsync("tenant-a", "brmf|BRMF99-0");

        Assert.Equal(ReprocessStatus.NotInOrigin, status);
        Assert.Empty(queue.Enqueued);
        Assert.Empty(Log.Recorded);   // recusado não conta
    }

    [Fact]
    public async Task Note_of_another_tenant_is_not_found_without_asking_any_origin()
    {
        var d365 = new FakeDiscovery("Dynamics365", 0);
        var queue = new FakeQueue();

        ReprocessStatus status = await Reprocess(d365, null, queue).ReprocessAsync("tenant-b", D365Key);

        Assert.Equal(ReprocessStatus.OtherTenant, status);
        Assert.Empty(d365.Asked);
        Assert.Empty(queue.Enqueued);
        Assert.Empty(Log.Recorded);
    }

    private RecordingLog Log { get; } = new();

    private DocumentReprocess Reprocess(IDocumentDiscovery d365, IDocumentDiscovery? fallback, FakeQueue queue)
        => new(Resolver("Dynamics365", [d365], fallback), queue, Log, new Tenant("tenant-a"));

    private sealed class RecordingLog : IReprocessLog
    {
        public List<(string Tenant, string Key)> Recorded { get; } = [];

        public Task RecordAsync(string tenantId, string naturalKey, CancellationToken ct = default)
        {
            Recorded.Add((tenantId, naturalKey));
            return Task.CompletedTask;
        }
    }

    private sealed class Tenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
