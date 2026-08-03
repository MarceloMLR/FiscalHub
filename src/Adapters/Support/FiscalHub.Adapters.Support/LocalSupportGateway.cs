using FiscalHub.Application.Support;
using Microsoft.Extensions.Logging;

namespace FiscalHub.Adapters.Support;

/// <summary>
/// Adapter de chamados mock (dev/demo): não chama provider nenhum — registra o "chamado" no log e
/// devolve um id/URL fake. Deixa exercitar todo o fluxo (porta, zip, UI) sem conta Freshdesk. Em
/// produção o perfil aponta para o adapter real; aqui, para "Local".
/// </summary>
internal sealed class LocalSupportGateway : ISupportTicketGateway
{
    private readonly ILogger<LocalSupportGateway> _log;

    public LocalSupportGateway(ILogger<LocalSupportGateway> log) => _log = log;

    public string Name => "Local";

    public Task<TicketResult> OpenAsync(SupportTicket ticket, string settingsJson, CancellationToken ct = default)
    {
        string id = $"LOCAL-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        long bytes = ticket.Attachments.Sum(a => (long)a.Content.Length);
        _log.LogInformation(
            "Chamado (mock) aberto: {Id} · tenant {Tenant} · '{Subject}' · {Notes} anexo(s), {Bytes} bytes",
            id, ticket.TenantId, ticket.Subject, ticket.Attachments.Count, bytes);

        return Task.FromResult(new TicketResult(id, $"local://tickets/{id}"));
    }
}
