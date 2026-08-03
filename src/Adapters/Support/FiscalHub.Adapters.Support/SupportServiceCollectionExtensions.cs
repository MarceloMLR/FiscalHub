using FiscalHub.Application.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Support;

/// <summary>
/// Registro no DI dos adapters de chamados. Todos os gateways entram como <c>ISupportTicketGateway</c>;
/// o <c>SupportTicketService</c> escolhe em runtime pelo <c>SupportAdapter</c> do perfil do tenant.
/// </summary>
public static class SupportServiceCollectionExtensions
{
    public static IServiceCollection AddSupportTicketAdapters(this IServiceCollection services)
    {
        services.AddHttpClient();   // IHttpClientFactory para o Freshdesk
        services.AddSingleton<ISupportTicketGateway, LocalSupportGateway>();
        services.AddSingleton<ISupportTicketGateway, FreshdeskSupportGateway>();
        return services;
    }
}
