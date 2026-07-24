using FiscalHub.Application.Directory;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Directory.Json;

/// <summary>Registro no DI do diretório em JSON. Único ponto público.</summary>
public static class CompanyDirectoryServiceCollectionExtensions
{
    /// <summary>Registra o <c>ICompanyDirectory</c> lido de um arquivo JSON (dev local).</summary>
    public static IServiceCollection AddJsonCompanyDirectory(this IServiceCollection services, Action<JsonCompanyDirectoryOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ICompanyDirectory, JsonCompanyDirectory>();
        return services;
    }
}
