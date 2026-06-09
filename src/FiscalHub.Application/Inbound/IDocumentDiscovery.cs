namespace FiscalHub.Application.Inbound;

/// <summary>
/// Localiza documentos na origem por critérios (modo pull) e devolve suas referências, sem o
/// conteúdo. Implementada por adapters que consultam a origem; adapters por evento não a usam.
/// </summary>
public interface IDocumentDiscovery
{
    /// <summary>Identificador da origem, usado pelo perfil do tenant para selecionar a implementação.</summary>
    string Origin { get; }

    /// <summary>Retorna as referências dos documentos que atendem aos critérios.</summary>
    Task<IReadOnlyList<DocumentReference>> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken ct = default);
}
