namespace FiscalHub.Application.Inbound;

/// <summary>
/// Resultado do fetch da fonte: o documento no domínio e a impressão do conteúdo cru (o que veio do
/// cliente, XML ou JSON). O hash alimenta a idempotência por conteúdo — se o cliente corrigir uma
/// nota (ex.: nota de entrada com valor errado), o cru muda, o hash muda e a nota reintegra.
/// </summary>
public sealed record FetchResult<TDocument>
{
    /// <summary>O documento já traduzido para o modelo de domínio.</summary>
    public required TDocument Document { get; init; }

    /// <summary>Impressão (SHA-256) do conteúdo cru exatamente como chegou da origem.</summary>
    public required string ContentHash { get; init; }
}
