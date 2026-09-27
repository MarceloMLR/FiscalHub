namespace FiscalHub.Application.Inbound;

/// <summary>
/// Busca um documento completo na origem a partir de sua referência e o devolve no modelo de
/// domínio (o "fetch" do claim-check). Genérica no tipo do documento.
/// </summary>
public interface IInboundSource<TDocument>
{
    /// <summary>Identificador da origem, usado pelo perfil do tenant para selecionar a implementação.</summary>
    string Origin { get; }

    /// <summary>
    /// Confere o locator da referência pela regra da origem, sem tocar o armazenamento. Devolve o problema, ou
    /// <c>null</c> se o locator vale. Quem interpreta o locator é quem o lê (ADR-0028): a ingestão manual confere
    /// antes de enfileirar, e o <see cref="FetchAsync"/> confere de novo antes de ler, para qualquer caminho.
    /// </summary>
    string? CheckLocator(DocumentReference reference);

    /// <summary>Busca o documento referenciado e devolve o domínio junto da impressão do cru.</summary>
    Task<FetchResult<TDocument>> FetchAsync(DocumentReference reference, CancellationToken ct = default);
}
