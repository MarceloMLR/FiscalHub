using FiscalHub.Application.Connectors;
using FiscalHub.Application.Directory;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Directory.Json;

/// <summary>Registro no DI do diretório em JSON. Único ponto público.</summary>
public static class CompanyDirectoryServiceCollectionExtensions
{
    /// <summary>
    /// SÓ DESENVOLVIMENTO: registra o diretório em JSON como o fallback de desenvolvimento (change
    /// erp-company-directory-and-card-filters, D3), com a chave <see cref="InboundAdapterChoice.DevelopmentFallbackKey"/>, e
    /// não como implementação comum. Ele responde só pelo tenant cujo ERP não tem diretório próprio. O host chama isto
    /// apenas em <c>IsDevelopment()</c>: fora dele, o mock nunca chega a um tenant (ADR-0028).
    /// </summary>
    public static IServiceCollection UseJsonCompanyDirectoryAsDevelopmentFallback(this IServiceCollection services, Action<JsonCompanyDirectoryOptions> configure)
    {
        services.Configure(configure);
        services.AddKeyedSingleton<ICompanyDirectory, JsonCompanyDirectory>(InboundAdapterChoice.DevelopmentFallbackKey);
        return services;
    }
}
