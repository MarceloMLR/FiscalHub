using FiscalHub.Application.Connectors;
using FiscalHub.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalHub.Adapters.Discovery.Local.Tests;

/// <summary>
/// Especifica o registro do catálogo local como o fallback de desenvolvimento da descoberta por período (change
/// erp-company-directory-and-card-filters, D3): pela chave do fallback, e nunca como implementação comum, para não entrar na
/// escolha pelo adapter de entrada.
/// </summary>
public class LocalDocumentDiscoveryRegistrationTests
{
    [Fact]
    public async Task Registers_only_as_the_development_fallback()
    {
        var services = new ServiceCollection();
        services.UseLocalDocumentDiscoveryAsDevelopmentFallback();
        await using ServiceProvider sp = services.BuildServiceProvider();

        Assert.Empty(sp.GetServices<IDocumentDiscovery>());
        IDocumentDiscovery fallback = sp.GetRequiredKeyedService<IDocumentDiscovery>(InboundAdapterChoice.DevelopmentFallbackKey);
        Assert.IsType<LocalDocumentDiscovery>(fallback);
        Assert.Equal("Local", fallback.Origin);
    }
}
