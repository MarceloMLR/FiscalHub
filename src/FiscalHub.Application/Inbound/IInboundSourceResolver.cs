namespace FiscalHub.Application.Inbound;

/// <summary>
/// Escolhe, por documento, o adapter de entrada que sabe buscá-lo (ADR-0025): a origem da referência;
/// sem ela, o adapter de entrada do perfil do tenant. Assim um mesmo tenant recebe de mais de uma origem
/// na mesma execução (ex.: XML pelo drop e D365 pelo feed).
/// </summary>
public interface IInboundSourceResolver<TDocument>
{
    /// <summary>Devolve o source da origem da referência. Sem adapter para ela → <see cref="InboundSourceNotFoundException"/>.</summary>
    Task<IInboundSource<TDocument>> ResolveAsync(DocumentReference reference, CancellationToken ct = default);
}
