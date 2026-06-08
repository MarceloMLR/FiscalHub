namespace FiscalHub.Application.Inbound;

/// <summary>
/// Porta de ENTRADA (origem agnóstica). Dado uma referência, busca o documento completo e o
/// devolve já no modelo de domínio. É o "fetch" do claim-check.
///
/// Genérica em <typeparamref name="TDocument"/> para que a esteira seja escrita UMA vez e sirva a
/// qualquer tipo (mercadoria hoje; CT-e, NFS-e no futuro). Para um mesmo tipo pode haver várias
/// implementações (XML, D365...), selecionadas pelo perfil do tenant via <see cref="Origin"/>.
/// </summary>
public interface IInboundSource<TDocument>
{
    /// <summary>Identifica a origem (ex.: "Xml", "D365") para o perfil do tenant selecionar.</summary>
    string Origin { get; }

    Task<TDocument> FetchAsync(DocumentReference reference, CancellationToken ct = default);
}
