namespace FiscalHub.Application.Inbound;

/// <summary>
/// Porta de DESCOBERTA (modo pull). Dado um conjunto de critérios, devolve as referências dos
/// documentos que existem na origem — sem trazer o conteúdo ainda.
///
/// Implementada por adapters que consultam a origem (views de banco, OData do ERP...). Adapters
/// dirigidos por evento (push) não precisam dela: o evento já entrega a referência.
/// </summary>
public interface IDocumentDiscovery
{
    /// <summary>Identifica a origem (ex.: "D365", "Views") para o perfil do tenant selecionar.</summary>
    string Origin { get; }

    Task<IReadOnlyList<DocumentReference>> DiscoverAsync(
        DiscoveryCriteria criteria, CancellationToken ct = default);
}
