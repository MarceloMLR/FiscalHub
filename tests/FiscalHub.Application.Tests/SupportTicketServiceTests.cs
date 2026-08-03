using FiscalHub.Application.Connectors;
using FiscalHub.Application.Outbound;
using FiscalHub.Application.Queries;
using FiscalHub.Application.Support;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Application.Tests;

/// <summary>
/// Especifica a abertura de chamado: monta um zip de logs por nota, respeita o teto de 20MB, escolhe
/// o adapter pelo perfil do tenant e valida a seleção/campos. Tudo com fakes das portas.
/// </summary>
public class SupportTicketServiceTests
{
    [Fact]
    public async Task Opens_ticket_with_one_zip_per_note_and_calls_gateway()
    {
        var gateway = new RecordingGateway("Freshdesk");
        SupportTicketService service = Build(gateway, adapter: "Freshdesk",
            notes: [Note("k1", "101"), Note("k2", "102")],
            traces: key => [new TraceFile("source.xml", [1, 2, 3]), new TraceFile("domain.json", [4, 5])]);

        TicketResult result = await service.OpenAsync("tenant-a", ["k1", "k2"], "Falha", "Descrição do problema.");

        Assert.Equal("REC-1", result.Id);
        Assert.NotNull(gateway.Received);
        Assert.Equal(2, gateway.Received!.Attachments.Count);                 // um zip por nota
        Assert.All(gateway.Received.Attachments, a => Assert.EndsWith(".zip", a.FileName));
        Assert.Contains("Descrição do problema.", gateway.Received.DescriptionText);
        Assert.Contains("101", gateway.Received.DescriptionText);             // infos de integração da nota
    }

    [Fact]
    public async Task Note_without_trace_files_produces_no_attachment_but_still_opens()
    {
        var gateway = new RecordingGateway("Freshdesk");
        SupportTicketService service = Build(gateway, adapter: "Freshdesk",
            notes: [Note("k1", "101")],
            traces: _ => []);   // sem arquivos de rastreabilidade

        await service.OpenAsync("tenant-a", ["k1"], "Falha", "Sem logs.");

        Assert.NotNull(gateway.Received);
        Assert.Empty(gateway.Received!.Attachments);
    }

    [Fact]
    public async Task Requires_at_least_one_note()
    {
        SupportTicketService service = Build(new RecordingGateway("Freshdesk"), "Freshdesk", [], _ => []);

        await Assert.ThrowsAsync<SupportTicketException>(
            () => service.OpenAsync("tenant-a", [], "Falha", "x"));
    }

    [Theory]
    [InlineData("", "desc")]
    [InlineData("assunto", "")]
    [InlineData("   ", "desc")]
    public async Task Requires_subject_and_description(string subject, string description)
    {
        var service = Build(new RecordingGateway("Freshdesk"), "Freshdesk", [Note("k1", "1")], _ => [new TraceFile("s.xml", [1])]);

        await Assert.ThrowsAsync<SupportTicketException>(
            () => service.OpenAsync("tenant-a", ["k1"], subject, description));
    }

    [Fact]
    public async Task Fails_when_tenant_has_no_support_adapter_configured()
    {
        SupportTicketService service = Build(new RecordingGateway("Freshdesk"), adapter: null,
            notes: [Note("k1", "1")], traces: _ => [new TraceFile("s.xml", [1])]);

        SupportTicketException ex = await Assert.ThrowsAsync<SupportTicketException>(
            () => service.OpenAsync("tenant-a", ["k1"], "Falha", "x"));
        Assert.Contains("não está configurada", ex.Message);
    }

    [Fact]
    public async Task Fails_when_configured_adapter_is_not_registered()
    {
        // Perfil pede "Freshdesk", mas só o "Local" está registrado.
        SupportTicketService service = Build(new RecordingGateway("Local"), adapter: "Freshdesk",
            notes: [Note("k1", "1")], traces: _ => [new TraceFile("s.xml", [1])]);

        await Assert.ThrowsAsync<SupportTicketException>(
            () => service.OpenAsync("tenant-a", ["k1"], "Falha", "x"));
    }

    [Fact]
    public async Task Extra_attachment_over_limit_is_rejected()
    {
        var service = Build(new RecordingGateway("Freshdesk"), "Freshdesk", [Note("k1", "1")], _ => [new TraceFile("s.xml", [1])]);
        var tooBig = new TicketAttachment("grande.bin", new byte[20 * 1024 * 1024 + 1]);   // > 20MB

        SupportTicketException ex = await Assert.ThrowsAsync<SupportTicketException>(
            () => service.OpenAsync("tenant-a", ["k1"], "Falha", "x", [tooBig]));
        Assert.Contains("20", ex.Message);
    }

    [Fact]
    public async Task Estimate_returns_zip_size_for_notes_and_zero_for_empty_selection()
    {
        var service = Build(new RecordingGateway("Freshdesk"), "Freshdesk",
            [Note("k1", "1")], _ => [new TraceFile("source.xml", [1, 2, 3, 4, 5])]);

        long bytes = await service.EstimateLogsBytesAsync("tenant-a", ["k1"]);
        long zero = await service.EstimateLogsBytesAsync("tenant-a", []);

        Assert.True(bytes > 0);
        Assert.Equal(0, zero);
    }

    // ── helpers ──
    private static SupportTicketService Build(
        ISupportTicketGateway gateway,
        string? adapter,
        IReadOnlyList<DocumentSummary> notes,
        Func<string, IReadOnlyList<TraceFile>> traces)
        => new(new FakeProfiles(adapter), new FakeQueries(notes), new FakeTraces(traces), [gateway]);

    private static DocumentSummary Note(string key, string number) => new()
    {
        TenantId = "tenant-a",
        NaturalKey = key,
        Type = DocumentType.GoodsInvoice55,
        Status = IntegrationStatus.IntegrationError,
        Attempts = 1,
        Number = number,
        Reason = "rejeitado",
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class FakeProfiles(string? adapter) : IConnectorProfileStore
    {
        public Task<TenantConnectorProfile?> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantConnectorProfile?>(new TenantConnectorProfile
            {
                TenantId = tenantId,
                Environment = "Sandbox",
                Realtime = false,
                InboundAdapter = "Xml",
                OutboundAdapter = "Avalara",
                SupportAdapter = adapter,
                SupportSettings = "{}",
            });

        public Task UpsertAsync(TenantConnectorProfile profile, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeQueries(IReadOnlyList<DocumentSummary> notes) : IDocumentQueries
    {
        public Task<IReadOnlyList<DocumentSummary>> ListByKeysAsync(string tenantId, IReadOnlyList<string> naturalKeys, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentSummary>>(notes.Where(n => naturalKeys.Contains(n.NaturalKey)).ToList());

        public Task<IReadOnlyList<DocumentSummary>> ListRecentAsync(int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DocumentGroup>> ListGroupsAsync(int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DocumentSummary>> ListByGroupAsync(string c, string b, string d, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeTraces(Func<string, IReadOnlyList<TraceFile>> traces) : INoteTraceReader
    {
        public Task<IReadOnlyList<TraceFile>> ReadAsync(string tenantId, string naturalKey, CancellationToken ct = default)
            => Task.FromResult(traces(naturalKey));
    }

    private sealed class RecordingGateway(string name) : ISupportTicketGateway
    {
        public string Name => name;
        public SupportTicket? Received { get; private set; }

        public Task<TicketResult> OpenAsync(SupportTicket ticket, string settingsJson, CancellationToken ct = default)
        {
            Received = ticket;
            return Task.FromResult(new TicketResult("REC-1", "rec://1"));
        }
    }
}
