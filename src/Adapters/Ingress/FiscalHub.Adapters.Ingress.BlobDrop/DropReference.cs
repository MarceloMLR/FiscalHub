using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Ingress.BlobDrop;

/// <summary>
/// Referência publicada para um drop ingerido. O drop é sempre de XML de NF-e, então a origem é a do
/// adapter de XML (<c>Xml</c>, ADR-0025) — mesmo que o perfil do tenant aponte outro ERP para o feed.
/// </summary>
internal static class DropReference
{
    public const string Origin = "Xml";

    public static DocumentReference For(string tenant, string key, string locator) => new()
    {
        TenantId = tenant,
        Type = DocumentType.GoodsInvoice55,
        NaturalKey = key,
        Locator = locator,
        Origin = Origin,
    };
}
