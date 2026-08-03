namespace FiscalHub.Application.Support;

/// <summary>
/// Porta do provider de chamados (Freshdesk, D365 Customer Service, mock…). Cada adapter se
/// identifica por <see cref="Name"/> — o serviço escolhe o certo pelo perfil do tenant. As settings
/// (domínio, credenciais por referência) chegam como JSON, no schema de cada adapter.
/// </summary>
public interface ISupportTicketGateway
{
    /// <summary>Nome do adapter (casa com <c>SupportAdapter</c> no perfil): ex. "Freshdesk", "Local".</summary>
    string Name { get; }

    Task<TicketResult> OpenAsync(SupportTicket ticket, string settingsJson, CancellationToken ct = default);
}
