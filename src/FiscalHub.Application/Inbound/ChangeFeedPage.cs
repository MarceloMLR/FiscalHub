namespace FiscalHub.Application.Inbound;

/// <summary>Uma página do feed de mudanças: os documentos descobertos e até onde a origem garante a entrega.</summary>
public sealed record ChangeFeedPage
{
    /// <summary>Documentos da página: a referência (claim-check, sem o conteúdo) e o carimbo de alteração de cada um.</summary>
    public required IReadOnlyList<ChangeFeedItem> Items { get; init; }

    /// <summary>
    /// Marca alta garantida: tudo o que mudou até este instante já foi entregue por esta página ou pelas
    /// anteriores. <c>null</c> = a página não garante nada novo (ex.: leitura vazia sem relógio da origem),
    /// e a marca não avança.
    /// </summary>
    public DateTimeOffset? HighWatermark { get; init; }

    /// <summary>
    /// Horizonte estável: nenhuma gravação futura na origem recebe carimbo menor ou igual a este instante,
    /// então um par (documento, carimbo) até aqui é definitivo e pode ser suprimido na releitura da
    /// sobreposição (design D16). Quem calcula é a origem, que conhece a resolução do próprio carimbo.
    /// <c>null</c> = nada nesta página é definitivo, e nada dela é suprimível.
    /// </summary>
    public DateTimeOffset? StableThrough { get; init; }
}

/// <summary>Um documento descoberto: a referência e o carimbo de alteração que a origem informou para ele.</summary>
public sealed record ChangeFeedItem(DocumentReference Reference, DateTimeOffset ChangedAt);
