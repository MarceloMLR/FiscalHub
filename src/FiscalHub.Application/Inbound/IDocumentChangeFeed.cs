namespace FiscalHub.Application.Inbound;

/// <summary>
/// Feed de mudanças de uma origem (modo delta): devolve, em páginas, as referências dos documentos que
/// mudaram depois de um instante. Diferente do <see cref="IDocumentDiscovery"/>, que busca por período e
/// não guarda estado: aqui quem guarda a marca d'água é o worker, e uma falha não pode avançá-la
/// (ADR-0024). A paginação (ex.: keyset no D365) fica inteira dentro do adapter.
/// </summary>
public interface IDocumentChangeFeed
{
    /// <summary>Identificador da origem; casa com o adapter de entrada do perfil do tenant (ex.: "Dynamics365").</summary>
    string Origin { get; }

    /// <summary>
    /// Lê o que mudou depois de <paramref name="since"/> (a sobreposição já vem descontada pelo worker).
    /// Cada página traz as referências e a marca alta que ela garante. Uma exceção no meio da leitura
    /// vale como falha: o worker mantém a marca na última página confirmada.
    /// </summary>
    IAsyncEnumerable<ChangeFeedPage> PullAsync(string tenantId, DateTimeOffset since, CancellationToken ct = default);
}
