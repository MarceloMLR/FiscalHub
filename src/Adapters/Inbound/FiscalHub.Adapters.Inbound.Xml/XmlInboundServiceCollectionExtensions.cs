using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>Registro no DI do adapter de entrada por XML. Único ponto público; os tipos ficam internal.</summary>
public static class XmlInboundServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>IInboundSource&lt;GoodsInvoice&gt;</c> que lê o XML do Blob. Requer um
    /// <c>BlobServiceClient</c> registrado pelo composition root (a connection string é dele).
    /// </summary>
    public static IServiceCollection AddXmlGoodsInvoiceSource(this IServiceCollection services)
    {
        services.TryAddSingleton<NfeXmlParser>();
        services.TryAddSingleton<IBlobReader, AzureBlobReader>();
        services.AddSingleton<IInboundSource<GoodsInvoice>, XmlGoodsInvoiceSource>();
        return services;
    }
}
