namespace FiscalHub.Application.Inbound;

/// <summary>
/// Busca um documento completo na origem a partir de sua referência e o devolve no modelo de
/// domínio (o "fetch" do claim-check). Genérica no tipo do documento.
/// </summary>
public interface IInboundSource<TDocument>
{
    /// <summary>Identificador da origem, usado pelo perfil do tenant para selecionar a implementação.</summary>
    string Origin { get; }

    /// <summary>Busca o documento referenciado e devolve o domínio junto da impressão do cru.</summary>
    Task<FetchResult<TDocument>> FetchAsync(DocumentReference reference, CancellationToken ct = default);
}
