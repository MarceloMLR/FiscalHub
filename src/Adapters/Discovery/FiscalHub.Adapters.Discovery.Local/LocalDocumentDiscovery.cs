using FiscalHub.Application.Inbound;
using FiscalHub.Domain.Envelope;

namespace FiscalHub.Adapters.Discovery.Local;

/// <summary>
/// Descoberta pull para dev local: representa a busca que, em produção, um adapter faria na origem
/// (Avalara/ERP) por período. Aqui o "catálogo" é fixo e casa com os XMLs semeados no Blob — filtra
/// por tenant/empresa/filial/período e devolve as referências, que o conector enfileira no padrão
/// claim-check. Trocar por Avalara/ERP é escrever outro adapter desta mesma porta.
/// </summary>
internal sealed class LocalDocumentDiscovery : IDocumentDiscovery
{
    public string Origin => "Local";

    // Catálogo de dev: espelha LocalSeed. Chave de acesso da NF-e = NaturalKey (idempotência real);
    // Locator aponta pro Blob de seed, de onde a esteira faz o fetch.
    private static readonly SeededDocument[] Catalog =
    [
        new()
        {
            Tenant = "tenant-a",
            Company = "12345678",
            Branch = "0001",
            Number = "123",
            IssuedAt = DateTimeOffset.Parse("2026-06-01T10:00:00-03:00"),
            AccessKey = "35260612345678000190550010000001231000000123",
            Locator = "nfe/nfe-exemplo.xml",
        },
        new()
        {
            Tenant = "tenant-a",
            Company = "98765432",
            Branch = "0001",
            Number = "456",
            IssuedAt = DateTimeOffset.Parse("2026-06-02T14:30:00-03:00"),
            AccessKey = "35260698765432000188550010000004561000000456",
            Locator = "nfe/nfe-exemplo-2.xml",
        },
    ];

    public Task<IReadOnlyList<DocumentReference>> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken ct = default)
    {
        IReadOnlyList<DocumentReference> matches = Catalog
            .Where(d => d.Tenant == criteria.TenantId)
            .Where(d => d.IssuedAt >= criteria.Start && d.IssuedAt <= criteria.End)
            .Where(d => criteria.Company is null || d.Company == criteria.Company)
            .Where(d => criteria.Establishment is null || d.Branch == criteria.Establishment)
            .Where(d => criteria.DocumentNumber is null || d.Number == criteria.DocumentNumber)
            .Select(d => new DocumentReference
            {
                TenantId = d.Tenant,
                Type = DocumentType.GoodsInvoice55,
                NaturalKey = d.AccessKey,
                Locator = d.Locator,
            })
            .ToList();

        return Task.FromResult(matches);
    }

    private sealed record SeededDocument
    {
        public required string Tenant { get; init; }
        public required string Company { get; init; }
        public required string Branch { get; init; }
        public required string Number { get; init; }
        public required DateTimeOffset IssuedAt { get; init; }
        public required string AccessKey { get; init; }
        public required string Locator { get; init; }
    }
}
