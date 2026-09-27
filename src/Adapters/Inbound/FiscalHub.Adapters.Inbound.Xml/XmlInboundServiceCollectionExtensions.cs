using FiscalHub.Application.Inbound;
using FiscalHub.Application.Tracing;
using FiscalHub.Domain.Goods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FiscalHub.Adapters.Inbound.Xml;

/// <summary>Registro no DI do adapter de entrada por XML. Único ponto público; os tipos ficam internal.</summary>
public static class XmlInboundServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>IInboundSource&lt;GoodsInvoice&gt;</c> que lê o XML do Blob. Requer um
    /// <c>BlobServiceClient</c> registrado pelo composition root (a connection string é dele). O locator só vale
    /// dentro de <c>{inboxContainer}/{tenant}/</c> (ADR-0028) — o mesmo container para onde o drop move os arquivos.
    /// </summary>
    public static IServiceCollection AddXmlGoodsInvoiceSource(
        this IServiceCollection services, string inboxContainer = XmlLocator.DefaultInboxContainer)
    {
        services.TryAddSingleton<NfeXmlParser>();
        services.TryAddSingleton<IBlobReader, AzureBlobReader>();
        services.TryAddSingleton<IProcessingTrace, NoOpProcessingTrace>();
        services.AddSingleton<IInboundSource<GoodsInvoice>>(sp => new XmlGoodsInvoiceSource(
            sp.GetRequiredService<IBlobReader>(),
            sp.GetRequiredService<NfeXmlParser>(),
            sp.GetRequiredService<IProcessingTrace>(),
            inboxContainer));
        return services;
    }
}
